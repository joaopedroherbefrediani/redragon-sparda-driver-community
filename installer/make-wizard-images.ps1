Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase
$dir = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent $dir
$theme = [IO.File]::ReadAllText((Join-Path $root 'src\SpardaControl\Themes\Theme.xaml'))
$logo = [regex]::Match($theme, '<Geometry x:Key="RedragonLogo">([^<]+)</Geometry>').Groups[1].Value
if (-not $logo) { throw 'RedragonLogo not found in Theme.xaml' }

function Save-Bmp([string]$xaml, [int]$w, [int]$h, [string]$path) {
    $el = [Windows.Markup.XamlReader]::Parse($xaml)
    $el.Measure([Windows.Size]::new($w, $h)); $el.Arrange([Windows.Rect]::new(0, 0, $w, $h)); $el.UpdateLayout()
    $rtb = New-Object Windows.Media.Imaging.RenderTargetBitmap $w, $h, 96, 96, ([Windows.Media.PixelFormats]::Pbgra32)
    $rtb.Render($el)
    $enc = New-Object Windows.Media.Imaging.BmpBitmapEncoder
    $enc.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create((New-Object Windows.Media.Imaging.FormatConvertedBitmap $rtb, ([Windows.Media.PixelFormats]::Bgr24), $null, 0)))
    $fs = [IO.File]::Create($path); $enc.Save($fs); $fs.Close()
}

$ns = "xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'"
$logoTile = @"
<Border Width='{0}' Height='{0}' CornerRadius='{1}'>
  <Border.Background>
    <LinearGradientBrush StartPoint='0,0' EndPoint='1,1'>
      <GradientStop Color='#FF464C' Offset='0'/>
      <GradientStop Color='#B01016' Offset='1'/>
    </LinearGradientBrush>
  </Border.Background>
  <Path Fill='White' Stretch='Uniform' Width='{2}' Height='{2}' Data='$logo'/>
</Border>
"@

$large = @"
<Grid $ns Width='328' Height='628' Background='#0B0C0F'>
  <Ellipse Width='640' Height='560' Margin='-160,-120,0,0' HorizontalAlignment='Left' VerticalAlignment='Top'>
    <Ellipse.Fill>
      <RadialGradientBrush>
        <GradientStop Color='#96781016' Offset='0'/>
        <GradientStop Color='#000B0C0F' Offset='1'/>
      </RadialGradientBrush>
    </Ellipse.Fill>
  </Ellipse>
  <StackPanel VerticalAlignment='Top' Margin='0,150,0,0'>
    $(($logoTile -f 120, 28, 86) -replace '<Border ', "<Border HorizontalAlignment='Center' ")
    <TextBlock Text='SPARDA' FontFamily='Segoe UI' FontWeight='Bold' FontSize='30' Foreground='White' HorizontalAlignment='Center' Margin='0,28,0,0'/>
    <TextBlock Text='C O N T R O L' FontFamily='Segoe UI' FontWeight='Bold' FontSize='15' Foreground='#E8262C' HorizontalAlignment='Center' Margin='0,2,0,0'/>
  </StackPanel>
  <TextBlock Text='Configurador offline&#x0a;para mouses Redragon' FontFamily='Segoe UI' FontSize='14' Foreground='#8C929E'
             TextAlignment='Center' HorizontalAlignment='Center' VerticalAlignment='Bottom' Margin='0,0,0,58'/>
</Grid>
"@
Save-Bmp $large 328 628 (Join-Path $dir 'wizard-large.bmp')

$small = "<Grid $ns Width='110' Height='110' Background='#202020'>" + (($logoTile -f 88, 21, 64) -replace '<Border ', "<Border HorizontalAlignment='Center' VerticalAlignment='Center' ") + '</Grid>'
Save-Bmp $small 110 110 (Join-Path $dir 'wizard-small.bmp')
"Wizard images written to $dir"
