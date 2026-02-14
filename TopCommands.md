# Top PowerShell 5.1 Commands — PoSHBlox Integration Tracker

Cross-referenced from multiple "most-used" lists (Stackify, LazyAdmin, TechTarget, TEKSpace, PDQ, Petri).

## Already in PoSHBlox (40 cmdlets)

### File / Folder (7)
- [x] Get-ChildItem
- [x] Copy-Item
- [x] Move-Item
- [x] Remove-Item
- [x] Get-Content
- [x] Set-Content
- [x] Get-Acl

### Process / Service (6)
- [x] Get-Process
- [x] Stop-Process
- [x] Get-Service
- [x] Start-Service
- [x] Stop-Service
- [x] Restart-Service

### Registry (4)
- [x] Get-ItemProperty
- [x] Set-ItemProperty
- [x] New-Item (Reg Key)
- [x] Remove-ItemProperty

### Network / Remote (5)
- [x] Test-Connection
- [x] Invoke-Command
- [x] Test-NetConnection
- [x] Get-NetAdapter
- [x] Get-NetIPAddress

### String / Data (9)
- [x] Where-Object
- [x] Select-Object
- [x] Sort-Object
- [x] Group-Object
- [x] ForEach-Object
- [x] Export-Csv
- [x] ConvertTo-Json
- [x] Select-String
- [x] Measure-Object

### Output (9)
- [x] Write-Host
- [x] Write-Output
- [x] Write-Warning
- [x] Write-Error
- [x] Write-Verbose
- [x] Out-Host
- [x] Out-Null
- [x] Format-Table
- [x] Format-List

---

## Not in PoSHBlox — High Priority (frequently cited across multiple sources)

### Web / API
- [ ] Invoke-WebRequest — HTTP requests (curl/wget equivalent)
- [ ] Invoke-RestMethod — REST API calls with auto-deserialization

### Data Conversion
- [ ] ConvertFrom-Json — Parse JSON input
- [ ] Import-Csv — Read CSV files
- [ ] ConvertTo-Html — Generate HTML reports
- [ ] ConvertFrom-Csv — Convert CSV string to objects
- [ ] Export-Clixml — Serialize objects to XML
- [ ] Import-Clixml — Deserialize objects from XML
- [ ] ConvertTo-Xml — Convert objects to XML

### File System
- [ ] Test-Path — Check if a path exists
- [ ] New-Item (File/Folder) — Create files and directories
- [ ] Out-File — Write pipeline output to file
- [ ] Rename-Item — Rename files or folders
- [ ] Add-Content — Append to a file
- [ ] Clear-Content — Clear file contents without deleting

### Process / Execution
- [ ] Start-Process — Launch programs or scripts
- [ ] Invoke-Expression — Execute a string as a command
- [ ] Start-Job — Run command as background job
- [ ] Get-Job — Get background job status
- [ ] Receive-Job — Get background job results
- [ ] Wait-Job — Wait for background job completion

### User Interaction
- [ ] Read-Host — Prompt user for input
- [ ] Write-Progress — Display a progress bar
- [ ] Write-Debug — Write debug-level output

### Object Manipulation
- [ ] Get-Member — Inspect object properties and methods
- [ ] New-Object — Create .NET or COM objects
- [ ] Add-Member — Add properties/methods to an object
- [ ] Compare-Object — Diff two object sets
- [ ] Tee-Object — Split pipeline (output + passthrough)
- [ ] Out-String — Convert pipeline to string

### Discovery / Help
- [ ] Get-Help — Display cmdlet documentation
- [ ] Get-Command — Search available commands
- [ ] Get-Alias — List command aliases

### Date / Time
- [ ] Get-Date — Get current date/time or format dates
- [ ] New-TimeSpan — Create a time duration
- [ ] Set-Date — Set the system date/time

### Variables / State
- [ ] Set-Variable — Create or update a variable
- [ ] Get-Variable — Retrieve variable info
- [ ] Remove-Variable — Delete a variable
- [ ] Clear-Variable — Clear a variable's value

### Security / Credentials
- [ ] Set-ExecutionPolicy — Set script execution policy
- [ ] Get-ExecutionPolicy — Get current execution policy
- [ ] ConvertTo-SecureString — Create secure string (passwords)
- [ ] Get-Credential — Prompt for username/password

### System / Environment
- [ ] Get-EventLog — Read Windows event logs
- [ ] Get-WinEvent — Read Windows event logs (newer)
- [ ] Set-Location — Change directory (cd)
- [ ] Get-Location — Get current directory (pwd)
- [ ] Get-ComputerInfo — System information
- [ ] Restart-Computer — Reboot a machine
- [ ] Stop-Computer — Shut down a machine

### Error Handling
- [ ] Set-StrictMode — Enforce coding rules
- [ ] Throw — Throw a terminating error

### Formatting / Display
- [ ] Out-GridView — Interactive GUI table
- [ ] Format-Wide — Format as wide table

---

## Summary

| Status | Count |
|--------|-------|
| Already integrated | 40 |
| High-priority additions | ~55 |
| **Total target** | **~95** |

This would cover the vast majority of cmdlets that appear in "top commands" lists and day-to-day scripting.
