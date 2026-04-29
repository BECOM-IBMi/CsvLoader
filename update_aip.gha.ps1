$assets = (get-location).path + "\assets"
$outpf = (get-location).path + "\artifacts"
$package = (get-location).path + "\installer"

$aipBase = $assets + "\SqlApiCliBase.aip"
$aip = $assets + "\SqlApiCli.aip"
$icon = $assets + "\SqlApiCli.ico"

cp $aipBase $aip

$advinst = New-Object -ComObject AdvancedInstaller

$project = $advinst.LoadProject($aip)

$ver = $args[0]
echo $ver

$project.ProductDetails.Version = $ver
$project.ProductDetails.SetIcon($icon)

$project.BuildComponent.Builds[0].OutputFolder = $package

$project.Build()
