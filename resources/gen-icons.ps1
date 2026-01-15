$resourcesDir = $PSScriptRoot
$numbers = 0..9
$chars = ,"qm"

foreach ($i in ($numbers + $chars))
{
    magick generated/icon-$i-b_16.png generated/icon-$i-b_32.png generated/icon-$i-b.ico
    magick generated/icon-$i-w_16.png generated/icon-$i-w_32.png generated/icon-$i-w.ico
    magick generated/icon-$i-g_16.png generated/icon-$i-g_32.png generated/icon-$i-g.ico
}

magick generated/icon_16.png generated/icon_32.png generated/icon_64.png generated/icon_128.png generated/icon.ico