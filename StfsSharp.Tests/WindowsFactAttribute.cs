namespace StfsSharp.Tests;

/// <summary>A fact that only runs on Windows, where host name rules differ.</summary>
internal sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "Windows-only host path rule.";
        }
    }
}
