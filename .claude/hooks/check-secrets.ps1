# PreToolUse hook: blocks Write and Edit tool calls that contain hardcoded secrets.
# Reads tool input JSON from stdin; exits 1 (blocking) if a forbidden pattern is found.

$raw = [Console]::In.ReadToEnd()
if (-not $raw) { exit 0 }

try {
    $data = $raw | ConvertFrom-Json
} catch {
    exit 0
}

# Extract the content being written or the replacement string being edited
$content = ""
if ($data.tool_name -eq "Write") {
    $content = $data.tool_input.content
} elseif ($data.tool_name -eq "Edit") {
    $content = $data.tool_input.new_string
}

if (-not $content) { exit 0 }

# Patterns that indicate a hardcoded secret
$patterns = @(
    '(?i)Server\s*=\s*[^;$]{3,};.*Password\s*=\s*[^;$"]{3,}',
    '(?i)password\s*=\s*"[^"$\{]{4,}"',
    '(?i)password\s*=\s*''[^''$\{]{4,}''',
    '(?i)"password"\s*:\s*"[^"$\{]{4,}"',
    '(?i)api[_\-]?key\s*=\s*"[^"$\{]{8,}"',
    '(?i)apikey\s*=\s*"[^"$\{]{8,}"',
    '(?i)secret\s*=\s*"[^"$\{]{8,}"',
    '(?i)connectionstring\s*=\s*"[^"$\{]{10,}"',
    '(?i)pwd\s*=\s*[^;,$\s"]{4,}',
    '(?i)Bearer\s+[A-Za-z0-9\-_]{20,}'
)

foreach ($pattern in $patterns) {
    if ($content -match $pattern) {
        $tool = $data.tool_name
        $file = $data.tool_input.file_path
        Write-Host ""
        Write-Host "BLOCKED by check-secrets hook"
        Write-Host "Tool   : $tool"
        Write-Host "File   : $file"
        Write-Host "Reason : Possible hardcoded secret detected (pattern matched: $pattern)"
        Write-Host "Fix    : Move the value to environment variables or IConfiguration — never hardcode secrets."
        Write-Host ""
        exit 1
    }
}

exit 0
