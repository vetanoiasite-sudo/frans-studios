using FransStudios.Api.Authentication;
using FransStudios.Api.Data;
using FransStudios.Api.Entities;
using FransStudios.Api.Features.Auth;
using Microsoft.EntityFrameworkCore;

namespace FransStudios.Api.Infrastructure;

/// <summary>
/// Maintenance commands, run on the server from the app folder:
///   FransStudios.Api.exe migrate
///   FransStudios.Api.exe create-admin &lt;username&gt;      (prompts for the password)
///   FransStudios.Api.exe reset-admin-password &lt;username&gt;
/// The password can also be supplied via the FS_ADMIN_PASSWORD environment variable for automation.
/// </summary>
public static class CliCommands
{
    public static async Task<bool> TryRunAsync(string[] args, IServiceProvider services)
    {
        var command = args[0].ToLowerInvariant();
        if (command is not ("migrate" or "create-admin" or "reset-admin-password")) return false;

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        switch (command)
        {
            case "migrate":
                await db.Database.MigrateAsync();
                Console.WriteLine("Database is up to date.");
                return true;

            case "create-admin":
            case "reset-admin-password":
            {
                if (args.Length < 2) { Console.Error.WriteLine($"Usage: {command} <username>"); Environment.ExitCode = 1; return true; }
                var username = AuthController.NormalizeUsername(args[1]);
                var passwords = scope.ServiceProvider.GetRequiredService<PasswordService>();
                var password = Environment.GetEnvironmentVariable("FS_ADMIN_PASSWORD") ?? ReadPassword();
                if (passwords.Validate(password) is { } error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; return true; }

                var user = await db.Users.SingleOrDefaultAsync(u => u.Username == username);
                if (command == "create-admin")
                {
                    if (user is not null) { Console.Error.WriteLine("That username already exists."); Environment.ExitCode = 1; return true; }
                    user = new User { Username = username, Role = UserRole.Admin };
                    db.Users.Add(user);
                }
                else if (user is null || user.Role != UserRole.Admin)
                {
                    Console.Error.WriteLine("Admin user not found."); Environment.ExitCode = 1; return true;
                }

                user.PasswordHash = passwords.Hash(user, password!);
                user.SecurityStamp = Guid.NewGuid();
                user.FailedLoginCount = 0;
                user.LockoutEndUtc = null;
                user.IsActive = true;
                await db.SaveChangesAsync();
                Console.WriteLine(command == "create-admin" ? $"Admin '{username}' created." : $"Password for '{username}' updated.");
                return true;
            }
        }
        return false;
    }

    private static string? ReadPassword()
    {
        if (Console.IsInputRedirected) return Console.ReadLine();
        Console.Write("Password: ");
        var chars = new List<char>();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) break;
            if (key.Key == ConsoleKey.Backspace) { if (chars.Count > 0) chars.RemoveAt(chars.Count - 1); continue; }
            chars.Add(key.KeyChar);
        }
        Console.WriteLine();
        return new string(chars.ToArray());
    }
}
