

namespace TrayPilot;

internal static partial class Diagnostics
{
    static int TestLiveSystemIcons(string report)
    {
        var log = new List<string>();
        using var session = new SystemIconSession();
        void Wait(int request)
        {
            for (int i = 0; i < 80 && (session.Acknowledged != request || session.Ticks == 0) && session.Error == 0; i++) Thread.Sleep(100);
            if (session.Acknowledged != request || session.Ticks == 0 || session.Error != 0)
                throw new IOException($"Native taskbar operation failed: 0x{session.Error:X8}");
        }
        try
        {
            Wait(session.Set(0));
            foreach (var (glyph, expected) in new[] { ('\uE74F',1), ('\uF8CC',2), ('\uEBC0',4), ('\uE361',16), ('\uE720',16),
                ('\uEC71',16), ('\uE37A',32), ('\uF47F',48), ('\uEABC',64), ('\uEC83',128), ('\uEADD',128),
                ('\uEB16',128), ('\uEF97',128), ('\uF1C6',128), ('\uE4D7',512), ('\uE97E',512), ('\uEE45',512),
                ('\uEE76',512), ('\uF2A3',1024), ('\uF285',1024), ('\uF2A5',1024), ('\uF2A8',1024), ('A',0) })
                if (session.IdentifyGlyph(glyph) != expected) throw new IOException($"Glyph classification failed: {((int)glyph):X4}");
            log.Add("PASS native glyph identification for privacy, language, Recall, Studio Effects and bell variants");
            int found = session.Found, original = session.Hidden;
            if (found == 0) throw new IOException("No live taskbar controls found.");
            log.Add($"Detected controls mask={found}, original hidden mask={original}, Explorer PID={session.Pid}");
            foreach (int bit in SystemIconCatalog.Items.Select(x => x.Mask))
            {
                if ((found & bit) == 0) { log.Add($"SKIP unavailable control {bit}"); continue; }
                if (session.SharedMicrophoneLocation && (bit & 48) != 0)
                {
                    Wait(session.Set(bit));
                    if ((session.Hidden & 48) != (original & 48)) throw new IOException("A partial privacy request hid a shared indicator");
                    Wait(session.Set(48));
                    if ((session.Hidden & 48) != 48) throw new IOException("Combined privacy request did not hide the shared indicator");
                    Wait(session.Set(0)); log.Add("PASS shared privacy indicator requires both requests"); continue;
                }
                Wait(session.Set(bit));
                if (session.Hidden != (original | bit)) throw new IOException($"Hide or sibling isolation failed for {bit}, hidden={session.Hidden}");
                log.Add($"PASS hide {bit} without changing siblings");
                Wait(session.Set(0));
                if (session.Hidden != original) throw new IOException($"Restore failed for {bit}");
                log.Add($"PASS restore {bit} to original visibility");
            }
            Wait(session.Set(found));
            if (session.Hidden != found) throw new IOException("Combined hide failed");
            log.Add("PASS combined hide");
            File.WriteAllLines(report, log);
            // Leave the final restoration to Dispose, then reconnect to verify it.
            session.Dispose(); Thread.Sleep(1000);
            using var verify = new SystemIconSession();
            for (int i = 0; i < 80 && verify.Ticks == 0; i++) Thread.Sleep(100);
            if (verify.Ticks == 0 || verify.Hidden != original) throw new IOException("Restore on session close failed");
            log.Add("PASS session close restores original controls");
            string ready = Path.GetFullPath(report) + ".ready";
            if (File.Exists(ready)) File.Delete(ready);
            using var child = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!)
            { UseShellExecute = false, CreateNoWindow = true, ArgumentList = { "--system-icons-crash-host", ready } })!;
            try
            {
                for (int i = 0; i < 200 && !File.Exists(ready) && !child.HasExited; i++) Thread.Sleep(100);
                if (!File.Exists(ready) || File.ReadAllText(ready) != found.ToString()) throw new IOException("Crash-test host did not hide controls");
                for (int i = 0; i < 50 && verify.Hidden != found; i++) Thread.Sleep(100);
                if (verify.Hidden != found) throw new IOException("Observer did not see the crash-test hidden state");
                child.Kill(); child.WaitForExit(5000);
                for (int i = 0; i < 50 && verify.Hidden != original; i++) Thread.Sleep(100);
                if (verify.Hidden != original) throw new IOException("Crash recovery did not restore controls");
                log.Add("PASS owner process crash restores original controls");
            }
            finally
            {
                if (!child.HasExited) { child.Kill(); child.WaitForExit(5000); }
                if (File.Exists(ready)) File.Delete(ready);
            }
            File.WriteAllLines(report, log); return 0;
        }
        finally { File.WriteAllLines(report, log); }
    }
}
