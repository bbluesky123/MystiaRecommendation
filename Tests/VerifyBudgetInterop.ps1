param(
    [Parameter(Mandatory = $true)]
    [string]$BepInExDir
)

$interopDir = Join-Path $BepInExDir "interop"
$coreDir = Join-Path $BepInExDir "core"
$dependencyPaths = @(
    (Join-Path $coreDir "Il2CppInterop.Runtime.dll"),
    (Join-Path $interopDir "Il2Cppmscorlib.dll"),
    (Join-Path $interopDir "UnityEngine.CoreModule.dll"),
    (Join-Path $interopDir "UnityEngine.dll"),
    (Join-Path $interopDir "DEYU.Singletons.dll"),
    (Join-Path $interopDir "Assembly-CSharp-firstpass.dll")
)

foreach ($dependencyPath in $dependencyPaths) {
    if (Test-Path -LiteralPath $dependencyPath) {
        [void][System.Reflection.Assembly]::LoadFrom($dependencyPath)
    }
}

$gameAssemblyPath = Join-Path $interopDir "Assembly-CSharp.dll"
if (-not (Test-Path -LiteralPath $gameAssemblyPath)) {
    throw "Game interop assembly not found: $gameAssemblyPath"
}

$gameAssembly = [System.Reflection.Assembly]::LoadFrom($gameAssemblyPath)
try {
    $gameTypes = $gameAssembly.GetTypes()
}
catch [System.Reflection.ReflectionTypeLoadException] {
    $gameTypes = $_.Exception.Types | Where-Object { $null -ne $_ }
}

function Get-RequiredType([string]$fullName) {
    $result = $gameTypes | Where-Object { $_.FullName -eq $fullName } | Select-Object -First 1
    if ($null -eq $result) {
        throw "Required type is missing: $fullName"
    }
    return $result
}

function Assert-Property($type, [string]$propertyName) {
    $flags = [System.Reflection.BindingFlags]"Public,NonPublic,Instance,Static"
    if ($null -eq $type.GetProperty($propertyName, $flags)) {
        throw "$($type.FullName) is missing property $propertyName"
    }
}

function Assert-Method($type, [string]$methodName) {
    $flags = [System.Reflection.BindingFlags]"Public,NonPublic,Instance,Static"
    if (-not ($type.GetMethods($flags) | Where-Object { $_.Name -eq $methodName })) {
        throw "$($type.FullName) is missing method $methodName"
    }
}

$guestType = Get-RequiredType "NightScene.GuestManagementUtility.SpecialGuestsController"
foreach ($propertyName in @("GetFund", "EnduranceLimit", "IsThisOrderFree", "CurrentOrderPropertySource")) {
    Assert-Property $guestType $propertyName
}

$orderType = Get-RequiredType "NightScene.GuestManagementUtility.GuestsManager+OrderBase"
foreach ($propertyName in @("FreeOrder", "Price")) {
    Assert-Property $orderType $propertyName
}

$orderPropertyType = Get-RequiredType "NightScene.GuestManagementUtility.GuestsManager+OrderProperty"
foreach ($propertyName in @("IsFree", "Source", "SourceLabel")) {
    Assert-Property $orderPropertyType $propertyName
}

$guestGroupType = Get-RequiredType "NightScene.GuestManagementUtility.GuestGroupController"
foreach ($methodName in @("set_GetFund", "RefreshCurrentFundAndOrder", "AddExtraOrderCount")) {
    Assert-Method $guestGroupType $methodName
}

$extraOrderMethod = $guestGroupType.GetMethods() |
    Where-Object { $_.Name -eq "AddExtraOrderCount" } |
    Select-Object -First 1
$parameterNames = @($extraOrderMethod.GetParameters() | ForEach-Object { $_.Name })
if (($parameterNames -join ",") -ne "num,isFree,source") {
    throw "AddExtraOrderCount parameters changed: $($parameterNames -join ',')"
}

$sellableType = Get-RequiredType "GameData.Core.Collections.Sellable"
Assert-Property $sellableType "Type"
$sellableKindType = Get-RequiredType "GameData.Core.Collections.Sellable+SellableType"
$sellableKinds = @([System.Enum]::GetNames($sellableKindType))
foreach ($requiredKind in @("Food", "Beverage")) {
    if ($sellableKinds -notcontains $requiredKind) {
        throw "SellableType is missing enum value $requiredKind"
    }
}

Write-Output "PASS game budget interop members are available"
Write-Output "PASS free extra-order parameter is available"
Write-Output "PASS tray items distinguish food from beverage"
