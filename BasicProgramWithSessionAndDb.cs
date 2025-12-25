using System.Diagnostics;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Collections.Concurrent;
using Microsoft.Win32.SafeHandles;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Http.HttpResults;

var builder = WebApplication.CreateBuilder(args);

// --- 1. Configuration Check ---
var connectionString = builder.Configuration.GetValue<string>("ConnectionString")
    ?? throw new Exception("CRITICAL: ConnectionString not found.");
var authSection = builder.Configuration.GetSection("AuthConfig");
if (!authSection.Exists()) throw new Exception("CRITICAL: AuthConfig section is missing.");

string expectedUser = authSection["Username"]!;
string expectedPass = authSection["Password"]!;
string targetDbName = authSection["TargetDB"]!;
string autoLoginSecret = authSection["AutoLoginSecret"]!;

// --- 2. Services ---
builder.Services.AddSingleton<LoginTracker>();
builder.Services.AddSingleton<SessionManager>();
builder.Services.AddSingleton<TerminalSessionRegistry>();
builder.Services.AddSingleton<DbLogger>(sp => new DbLogger(connectionString));

builder.Services.AddHttpClient();

builder.WebHost.UseUrls("http://127.0.0.1:8181");

var app = builder.Build();

var sessions = app.Services.GetRequiredService<SessionManager>();
var tracker = app.Services.GetRequiredService<LoginTracker>();


// FIX: Added Generic Types to dictionary
var activeSessions = new ConcurrentDictionary<string, Process>();

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseWebSockets();

// --- 3. Endpoints ---
app.MapPost("/login", (LoginRequest req, DbLogger logger) =>
{
    if (tracker.IsLockedOut()) {
        logger.Log("WARNING", $"Locked out login attempt for user: {req.User}");
        return Results.Problem(statusCode: 423, detail: "Locked out.");
    }
    if (req.User == expectedUser && req.Pass == expectedPass)
    {
        tracker.Reset();
        logger.Log("INFO", $"User {req.User} logged in successfully.");
        return Results.Ok(new { token = sessions.CreateToken() }); // Matches SessionManager now
    }
    tracker.RecordFailure();
    logger.Log("ERROR", $"Failed login attempt for user: {req.User}");
    return Results.Unauthorized();
});

app.MapPost("/auth-secret", (AutoLoginRequest req, DbLogger logger) =>
{
    if (tracker.IsLockedOut()) {
        logger.Log("WARNING", $"Locked out login attempt for secret: {req.Secret}");
        return Results.Problem(statusCode: 423, detail: "Locked out.");
    }

    if (req.Secret == autoLoginSecret)
    {
        tracker.Reset();
        logger.Log("INFO", $"Secret logged in successfully.");
        return Results.Ok(new { token = sessions.CreateToken() }); // Matches SessionManager now
    }
    tracker.RecordFailure();
        logger.Log("ERROR", $"Failed login attempt for secret: {req.Secret}");

    return Results.Unauthorized();
});


// --- Add this to your Endpoints section ---

app.Run();


// --- Classi di Supporto ---
public record LoginRequest(string User, string Pass);
public record AutoLoginRequest(string Secret);

public class SessionManager
{
    private readonly ConcurrentDictionary<string, DateTime> _s = new();
    public string CreateToken() { var t = Guid.NewGuid().ToString(); _s[t] = DateTime.UtcNow.AddHours(2); return t; }
    public bool IsValid(string t) => !string.IsNullOrEmpty(t) && _s.TryGetValue(t, out var exp) && exp > DateTime.UtcNow;
}

public class LoginTracker
{
    private int _f = 0; private DateTime? _l = null;
    public bool IsLockedOut() => _l.HasValue && DateTime.UtcNow < _l.Value;
    public void RecordFailure() { _f++; if (_f >= 5) _l = DateTime.UtcNow.AddMinutes(15); }
    public void Reset() { _f = 0; _l = null; }
}
 
public class DbLogger
{
    private readonly string _conn;
    public DbLogger(string conn) { _conn = conn; Init(); }
    private void Init()
    {
        using var c = new SqlConnection(_conn); c.Open();
        new SqlCommand("IF NOT EXISTS (SELECT * FROM sys.databases WHERE name='"+targetDbName+"') CREATE DATABASE ["+targetDbName+"]", c).ExecuteNonQuery();
        c.ChangeDatabase(targetDbName);
        new SqlCommand("IF NOT EXISTS (SELECT * FROM sys.objects WHERE name='ILogging') CREATE TABLE AppLogging (Id INT IDENTITY PRIMARY KEY, Timestamp DATETIME DEFAULT GETUTCDATE(), Level NVARCHAR(50), Message NVARCHAR(MAX), ConnectionId NVARCHAR(100))", c).ExecuteNonQuery();
    }
    public void Log(string level, string msg, string? connId = null)
    {
        try
        {
            using var c = new SqlConnection(_conn); c.Open(); c.ChangeDatabase(targetDbName);
            var cmd = new SqlCommand("INSERT INTO ILogging (Level, Message, ConnectionId) VALUES (@l, @m, @c)", c);
            cmd.Parameters.AddWithValue("@l", level); 
            cmd.Parameters.AddWithValue("@m", msg); 
            cmd.Parameters.AddWithValue("@c", connId == null ? "-" : connId);
            cmd.ExecuteNonQuery();
        }
        catch { }
    }
}
