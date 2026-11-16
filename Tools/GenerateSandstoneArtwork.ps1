# Reproducible original 16px sandstone artwork; existing atlas pixels are preserved.
Add-Type -AssemblyName System.Drawing
$path=Join-Path (Split-Path -Parent $PSScriptRoot) 'Content/Blocks/terrain.png'
$atlas=[Drawing.Bitmap]::new($path)
try {
 for($tile=81;$tile -le 83;$tile++) {for($y=0;$y -lt 16;$y++){for($x=0;$x -lt 16;$x++) {
  $grain=(($x*37+$y*67+$x*$y*13)%11)-5
  if($tile -eq 82){$r=222+$grain;$g=208+$grain;$b=163+$grain}
  elseif($tile -eq 83){$grain*=2;$r=199+$grain;$g=181+$grain;$b=133+$grain;if(($x*3+$y*5)%17 -lt 3){$r-=17;$g-=17;$b-=14}}
  else {$band=$y%5;$shade=if($band -eq 4){-24}elseif($band -eq 0){7}else{0};$r=216+$grain+$shade;$g=199+$grain+$shade;$b=151+$grain+$shade
   if(($y%5 -gt 1) -and (($x+([int][Math]::Floor($y/5)*7))%16 -eq 3)){$r-=10;$g-=10;$b-=8}
  }
  $atlas.SetPixel(($tile%16)*16+$x,[int][Math]::Floor($tile/16)*16+$y,[Drawing.Color]::FromArgb(255,$r,$g,$b))
 }}}
 $atlas.Save($path+'.sandstone.png',[Drawing.Imaging.ImageFormat]::Png)
}finally{$atlas.Dispose()}
Move-Item -LiteralPath ($path+'.sandstone.png') -Destination $path -Force
