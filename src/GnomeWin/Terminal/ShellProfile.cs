using GnomeWin.Services;
using GnomeWin.Services.Logging;

namespace GnomeWin.Terminal;

public static class ShellProfile
{
    public static string CommandLine()
    {
        string profile = EnsureProfile();
        return $"\"{PowerShellPath()}\" -NoLogo -NoExit -ExecutionPolicy Bypass -Command \". '{profile.Replace("'", "''")}'\"";
    }

    public static string PowerShellPath()
    {
        foreach (var dir in new[]
                 {
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerShell", "7"),
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps"),
                 })
        {
            string p = Path.Combine(dir, "pwsh.exe");
            if (File.Exists(p)) return p;
        }
        return Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
    }

    private static string EnsureProfile()
    {
        string dir = Path.Combine(AppPaths.Root, "console");
        string path = Path.Combine(dir, "gnomewin-console.ps1");
        try
        {
            Directory.CreateDirectory(dir);
            if (!File.Exists(path) || File.ReadAllText(path) != Script)
                File.WriteAllText(path, Script, new System.Text.UTF8Encoding(true));
        }
        catch (Exception ex) { Log.Warn("Cannot write the Console profile", ex); }
        return path;
    }

    public const string Script = """
        $global:__GwEsc = [char]27

        function global:prompt {
            $ok = $?
            $p = $ExecutionContext.SessionState.Path.CurrentLocation.ProviderPath
            if (-not $p) { $p = (Get-Location).Path }
            if ($p.StartsWith($HOME, [StringComparison]::OrdinalIgnoreCase)) { $p = '~' + $p.Substring($HOME.Length) }
            $p = $p -replace '\\', '/'
            $u = "$env:USERNAME@$env:COMPUTERNAME".ToLowerInvariant()
            $e = $global:__GwEsc
            $mark = if ($ok) { '$' } else { "$e[1;31m`$$e[0m" }
            "$e]0;${u}: $p$([char]7)$e[1;32m$u$e[0m:$e[1;34m$p$e[0m$mark "
        }

        function global:__GwClassify([string]$line) {
            $t = $line.Trim()
            if (-not $t) { return $null }
            $parts = $t -split '\s+', 2
            $first = $parts[0].ToLowerInvariant()
            $rest = if ($parts.Count -gt 1) { $parts[1].Trim() } else { '' }

            if ($first -in 'cd', 'chdir' -and $rest -match '^/d\s+(.+)$') { return @{ Kind = 'cd'; Arg = $Matches[1].Trim().Trim('"') } }
            if ($first -eq 'set') {
                if ($rest -match '^(?<n>[^=/\s"][^=]*)=(?<v>.*)$') { return @{ Kind = 'setenv'; Name = $Matches['n'].Trim(); Value = $Matches['v'] } }
                if ($rest -eq '' -or $rest -match '^/[ap]\b' -or $rest -notmatch '\s') { return @{ Kind = 'cmd' } }
            }
            if ($first -eq 'where' -and $rest -and $rest -notmatch '^[{(-]') { return @{ Kind = 'exe'; Exe = 'where.exe' } }
            if ($first -eq 'sc' -and $rest -match '^(query|queryex|start|stop|pause|continue|config|qc|create|delete|description|failure|sdshow)\b') { return @{ Kind = 'exe'; Exe = 'sc.exe' } }
            if ($first -eq 'curl' -and $PSVersionTable.PSVersion.Major -lt 6) { return @{ Kind = 'exe'; Exe = 'curl.exe' } }
            if ($first -in 'assoc', 'ftype', 'mklink', 'ver', 'vol', 'pause', 'path', 'verify') { return @{ Kind = 'cmd' } }
            if ($first -eq 'if' -and $rest -notmatch '^\(') { return @{ Kind = 'cmd' } }
            if ($first -eq 'for' -and $rest -match '^(/|%)') { return @{ Kind = 'cmd' } }
            if ($first -eq 'start' -and $rest -match '^""') { return @{ Kind = 'cmd' } }
            if ($first -in 'dir', 'copy', 'del', 'erase', 'move', 'ren', 'rename', 'rd', 'rmdir', 'md', 'mkdir', 'type', 'echo' -and
                $rest -match '(^|\s)/[a-z?][\w:-]*(\s|$)') { return @{ Kind = 'cmd' } }
            if ($t -match '%[a-z_][\w()~:,.-]*%') { return @{ Kind = 'cmd' } }
            if ($PSVersionTable.PSVersion.Major -lt 7 -and $t -match '&&|\|\|') { return @{ Kind = 'cmd' } }
            return $null
        }

        function global:__GwRunCmd([string]$line) {
            $psi = New-Object System.Diagnostics.ProcessStartInfo
            $psi.FileName = Join-Path $env:SystemRoot 'System32\cmd.exe'
            $psi.Arguments = '/d /c ' + $line
            $psi.UseShellExecute = $false
            $loc = Get-Location -PSProvider FileSystem
            if ($loc) { $psi.WorkingDirectory = $loc.ProviderPath }
            $proc = [System.Diagnostics.Process]::Start($psi)
            $proc.WaitForExit()
            $global:LASTEXITCODE = $proc.ExitCode
        }

        $global:__GwPending = $null
        $ExecutionContext.InvokeCommand.PreCommandLookupAction = {
            param($name, $e)
            $p = $global:__GwPending
            if (-not $p -or $e.CommandOrigin -ne 'Runspace' -or $name -ne $p.First) { return }
            $global:__GwPending = $null
            switch ($p.Kind) {
                'cmd'    { $l = $p.Line; $e.CommandScriptBlock = { __GwRunCmd $l }.GetNewClosure() }
                'cd'     { $a = $p.Arg; $e.CommandScriptBlock = { Set-Location -LiteralPath $a }.GetNewClosure() }
                'setenv' { $n = $p.Name; $v = $p.Value; $e.CommandScriptBlock = { Set-Item -LiteralPath ('env:' + $n) -Value $v }.GetNewClosure() }
                'exe'    { $x = $p.Exe; $e.CommandScriptBlock = { & $x @args }.GetNewClosure() }
            }
            $e.StopSearch = $true
        }

        function global:__GwAccept {
            $line = $null; $cursor = $null
            [Microsoft.PowerShell.PSConsoleReadLine]::GetBufferState([ref]$line, [ref]$cursor)
            $global:__GwPending = $null
            $c = __GwClassify $line
            if ($c) {
                $tokens = $null; $errors = $null
                [void][System.Management.Automation.Language.Parser]::ParseInput($line, [ref]$tokens, [ref]$errors)
                $t = $line.Trim()
                if ($c.Kind -eq 'cmd' -and ($errors.Count -gt 0 -or $t -match '[|<>&]')) {
                    $q = $t.Replace("'", "''")
                    $new = if ($t.Contains('"')) { "__GwRunCmd '$q'" } else { "cmd /c '$q'" }
                    [Microsoft.PowerShell.PSConsoleReadLine]::Replace(0, $line.Length, $new)
                } elseif ($errors.Count -eq 0) {
                    $c.Line = $t
                    $c.First = ($t -split '\s+', 2)[0]
                    $global:__GwPending = $c
                }
            }
            [Microsoft.PowerShell.PSConsoleReadLine]::AcceptLine()
        }

        if (Get-Module -ListAvailable -Name PSReadLine) {
            Import-Module PSReadLine -ErrorAction SilentlyContinue
            Set-PSReadLineKeyHandler -Key Enter -BriefDescription 'GnomeWinAcceptLine' -LongDescription 'Accept the line, running cmd.exe syntax with cmd.exe' -ScriptBlock { __GwAccept }
            Set-PSReadLineOption -BellStyle None -ErrorAction SilentlyContinue
        }
        """;
}
