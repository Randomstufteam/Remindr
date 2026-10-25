using System;
using Remindr.Setup.Core;

namespace Remindr.Setup;

/// <summary>/quiet: no UI. Exit codes: 0 done, 1 failed, 2 stopped (Remindr could not be closed).</summary>
public static class QuietRunner
{
    public static int Run(SetupArgs args, ISetupLog log)
    {
        try
        {
            var backend = new RealBackend(log);
            var prompts = new AutoYesPrompts();       // quiet means yes: close, then force close, Remindr if it is running

            if (args.Uninstall)
            {
                backend.Uninstall(UninstallPolicy.QuietRemoveData(args), prompts, _ => { });
                if (SelfDelete.InPlaceUninstallDone) SelfDelete.Schedule(SetupInfo.ExePath, log);
                return 0;
            }

            // On an update, keep whatever the person had chosen before.
            var existing = backend.Detect();
            if (args.StartWithWindows && !SetupFeatures.StartWithWindows)
                log.Info("startup option disabled in this version");
            var options = new InstallOptions
            {
                DesktopShortcut = args.DesktopShortcut || existing.HasDesktopShortcut,
                StartWithWindows = SetupFeatures.StartWithWindows && (args.StartWithWindows || existing.HasRunValue),
            };
            backend.Install(options, prompts, _ => { });
            return 0;
        }
        catch (SetupCancelledException ex)
        {
            log.Info("Quiet run stopped: " + ex.Message);
            return 2;
        }
        catch (Exception ex)
        {
            log.Error("Quiet run failed.", ex);
            return 1;
        }
    }
}
