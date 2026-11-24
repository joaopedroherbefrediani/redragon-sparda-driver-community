Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$theme = [IO.File]::ReadAllText((Join-Path $root 'src\SpardaControl\Themes\Theme.xaml'))
$logo = [regex]::Match($theme, '<Geometry x:Key="RedragonLogo">([^<]+)</Geometry>').Groups[1].Value
if (-not $logo) { throw 'RedragonLogo not found in Theme.xaml' }

function Render-Png([int]$s) {
    $pad = [Math]::Max(1, $s * 0.04)
    $inner = $s - 2 * $pad
    $xaml = @"
<Grid xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Width='$s' Height='$s'>
  <Border Margin='$pad' CornerRadius='$($inner * 0.23)'>
    <Border.Background>
      <LinearGradientBrush StartPoint='0,0' EndPoint='1,1'>
        <GradientStop Color='#FF464C' Offset='0'/>
        <GradientStop Color='#B01016' Offset='1'/>
      </LinearGradientBrush>
    </Border.Background>
    <Path Fill='White' Stretch='Uniform' Width='$($inner * 0.72)' Height='$($inner * 0.72)' Data='$logo'/>
  </Border>
</Grid>
"@
    $el = [Windows.Markup.XamlReader]::Parse($xaml)
    $el.Measure([Windows.Size]::new($s, $s)); $el.Arrange([Windows.Rect]::new(0, 0, $s, $s)); $el.UpdateLayout()
    $rtb = New-Object Windows.Media.Imaging.RenderTargetBitmap $s, $s, 96, 96, ([Windows.Media.PixelFormats]::Pbgra32)
    $rtb.Render($el)
    $enc = New-Object Windows.Media.Imaging.PngBitmapEncoder
    $enc.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($rtb))
    $ms = New-Object IO.MemoryStream
    $enc.Save($ms)
    return , $ms.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$images = foreach ($s in $sizes) { , (Render-Png $s) }
$out = Join-Path $root 'src\SpardaControl\Assets\app.ico'
$fs = [IO.File]::Create($out)
$bw = New-Object IO.BinaryWriter $fs
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $data = $images[$i]
    $dim = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$data.Length); $bw.Write([UInt32]$offset)
    $offset += $data.Length
}
foreach ($data in $images) { $bw.Write([byte[]]$data) }
$bw.Close()
"Icon written: $out"
