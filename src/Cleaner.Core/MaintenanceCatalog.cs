namespace Cleaner.Core;

public sealed record MaintenanceCommand(string Executable, string[] Arguments);
public sealed record MaintenanceOperation(string Id, string Title, string Description, string Impact,
    bool ChangesSystem, MaintenanceCommand[] Commands);

public static class MaintenanceCatalog
{
    private static MaintenanceCommand Dism(string action) => new("dism.exe", ["/Online", "/Cleanup-Image", action, "/NoRestart"]);
    private static MaintenanceCommand Power(params string[] arguments) => new("powercfg.exe", arguments);
    public static MaintenanceOperation[] All =>
    [
        new("analyze-components", "Analyze Windows component store", "Ask DISM to report the actual size of the Windows component store and whether cleanup is recommended.",
            "Read-only analysis. Administrator permission is required. Windows may take several minutes.", false, [Dism("/AnalyzeComponentStore")]),
        new("clean-components", "Clean superseded Windows components", "Use Windows servicing to remove superseded component versions. Never delete WinSxS files directly.",
            "Permanent Windows maintenance. Superseded versions are removed immediately rather than waiting for the normal grace period. /ResetBase is never used. Cannot be cancelled safely once started.", true, [Dism("/StartComponentCleanup")]),
        new("delivery-cache", "Clear Delivery Optimization downloads", "Use the Windows Delivery Optimization module to clear its download cache. Pinned downloads are retained.",
            "Permanently removes cached downloads; Windows may download them again. No update database or SoftwareDistribution folder is deleted.", true,
            [new("WindowsPowerShell\\v1.0\\powershell.exe", ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command",
                "$ErrorActionPreference='Stop'; $PSModuleAutoloadingPreference='None'; Import-Module (Join-Path $PSHOME 'Modules\\DeliveryOptimization\\DeliveryOptimization.psd1') -ErrorAction Stop; Delete-DeliveryOptimizationCache -Force -ErrorAction Stop; Write-Output 'Delivery Optimization cache cleared.'"])]),
        new("hibernate-reduced", "Reduce hibernation storage", "Switch to a reduced hibernation file using powercfg. This retains Fast Startup support.",
            "Full hibernation and hybrid sleep become unavailable. Power settings are changed. A failed second step can leave the hibernation file at its system-managed default size.", true, [Power("/hibernate", "/size", "0"), Power("/hibernate", "/type", "reduced")]),
        new("hibernate-off", "Disable hibernation", "Turn off hibernation through Windows and remove its reserved file.",
            "Disables hibernation, hybrid sleep, and Fast Startup. Consider laptop battery behavior before running. Enable hibernation again to restore support.", true, [Power("/hibernate", "off")]),
        new("hibernate-on", "Restore full hibernation", "Enable hibernation and switch its file back to the full type.",
            "Reserves disk space for hibernation again. Availability also depends on the device and its power policy.", true, [Power("/hibernate", "on"), Power("/hibernate", "/type", "full")])
    ];
    public static MaintenanceOperation Get(string id) => All.SingleOrDefault(x => x.Id == id)
        ?? throw new ArgumentException("Unknown maintenance operation.");
    public static string ExecutablePath(MaintenanceCommand command)
    {
        if (command.Executable is not ("dism.exe" or "powercfg.exe" or "WindowsPowerShell\\v1.0\\powershell.exe"))
            throw new ArgumentException("Executable is not permitted.");
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), command.Executable);
    }
}
