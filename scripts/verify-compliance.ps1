# Copyright © Erickson Lopez. MIT License.
[CmdletBinding()]
param (
    [string]$RootDirectory = "."
)
& "$PSScriptRoot\validate-compliance.ps1" @PSBoundParameters
exit $LASTEXITCODE
