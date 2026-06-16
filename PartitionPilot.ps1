# PartitionPilot.ps1 — Complete disk partition management tool
# Single-file, self-elevating, no dependencies beyond Windows 10+.

# ── Self-elevate ────────────────────────────────────────────────────────────────

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Start-Process powershell -Verb RunAs -ArgumentList "-ExecutionPolicy Bypass -File `"$PSCommandPath`""
    exit
}

# ── Hide console window ────────────────────────────────────────────────────────

Add-Type -Name ConsoleWin -Namespace Win32 -MemberDefinition @'
    [DllImport("kernel32.dll")] public static extern IntPtr GetConsoleWindow();
    [DllImport("user32.dll")]   public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
'@
[Win32.ConsoleWin]::ShowWindow([Win32.ConsoleWin]::GetConsoleWindow(), 0) | Out-Null

Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase

# ── XAML ────────────────────────────────────────────────────────────────────────

[xml]$xaml = @'
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="PartitionPilot" Width="1060" Height="760"
        MinWidth="800" MinHeight="600"
        WindowStartupLocation="CenterScreen" Background="#F0F2F5">
  <DockPanel>
    <!-- Status bar -->
    <Border DockPanel.Dock="Bottom" Background="#E8E8E8" Padding="10,5">
      <TextBlock x:Name="txtStatus" FontSize="11" Foreground="#555"/>
    </Border>

    <!-- Activity log -->
    <Border DockPanel.Dock="Bottom" Height="130" BorderBrush="#CCC"
            BorderThickness="0,1,0,0" Background="#1e1e2e" Padding="4">
      <Grid>
        <Grid.RowDefinitions>
          <RowDefinition Height="Auto"/>
          <RowDefinition Height="*"/>
        </Grid.RowDefinitions>
        <TextBlock Grid.Row="0" Text="  Activity Log" Foreground="#888"
                   FontSize="10" Margin="0,2,0,2"/>
        <TextBox x:Name="txtLog" Grid.Row="1" IsReadOnly="True"
                 VerticalScrollBarVisibility="Auto" FontFamily="Consolas"
                 FontSize="11" Background="Transparent" Foreground="#CCC"
                 BorderThickness="0" TextWrapping="Wrap"/>
      </Grid>
    </Border>

    <!-- Main content -->
    <DockPanel Margin="12,8,12,0">
      <!-- Title -->
      <StackPanel DockPanel.Dock="Top" Margin="0,0,0,8">
        <TextBlock Text="PartitionPilot" FontSize="24" FontWeight="SemiBold" Foreground="#1a1a2e"/>
        <TextBlock Text="Disk partition management for Windows" FontSize="11" Foreground="#888"/>
      </StackPanel>

      <!-- Tab control -->
      <TabControl x:Name="tabMain" TabStripPlacement="Left" BorderThickness="0" Background="Transparent">
        <TabControl.Resources>
          <Style TargetType="TabItem">
            <Setter Property="Padding" Value="14,10"/>
            <Setter Property="FontSize" Value="13"/>
            <Setter Property="MinWidth" Value="110"/>
            <Setter Property="Cursor" Value="Hand"/>
            <Setter Property="Template">
              <Setter.Value>
                <ControlTemplate TargetType="TabItem">
                  <Border x:Name="Bd" Padding="{TemplateBinding Padding}"
                          Background="Transparent" CornerRadius="4" Margin="0,1">
                    <ContentPresenter ContentSource="Header" HorizontalAlignment="Left"/>
                  </Border>
                  <ControlTemplate.Triggers>
                    <Trigger Property="IsSelected" Value="True">
                      <Setter TargetName="Bd" Property="Background" Value="#DBEAFE"/>
                      <Setter Property="Foreground" Value="#0078D4"/>
                      <Setter Property="FontWeight" Value="SemiBold"/>
                    </Trigger>
                    <Trigger Property="IsMouseOver" Value="True">
                      <Setter TargetName="Bd" Property="Background" Value="#EEE"/>
                    </Trigger>
                  </ControlTemplate.Triggers>
                </ControlTemplate>
              </Setter.Value>
            </Setter>
          </Style>
        </TabControl.Resources>

        <!-- ═══ TAB 1: PARTITIONS ═══ -->
        <TabItem Header="Partitions">
          <DockPanel Margin="8,4,0,0">
            <StackPanel DockPanel.Dock="Top" Orientation="Horizontal" Margin="0,0,0,6">
              <TextBlock Text="Disk:" VerticalAlignment="Center" FontWeight="SemiBold" Margin="0,0,8,0"/>
              <ComboBox x:Name="cmbDisk" Width="520" Height="28" FontSize="13"/>
              <Button x:Name="btnRefresh" Content="Refresh" Width="75" Height="28"
                      Margin="8,0,0,0" FontSize="12"/>
            </StackPanel>
            <Border DockPanel.Dock="Top" Height="68" BorderBrush="#CCC" BorderThickness="1"
                    CornerRadius="6" Margin="0,0,0,6" ClipToBounds="True" Background="#2C3E50">
              <Grid x:Name="grdDiskBar"/>
            </Border>
            <Border DockPanel.Dock="Bottom" Padding="6" Background="White"
                    BorderBrush="#CCC" BorderThickness="1" CornerRadius="4" Margin="0,6,0,0">
              <WrapPanel>
                <Button x:Name="btnCreate" Content="Create"  Margin="2" Width="72" Height="28" FontSize="12"/>
                <Button x:Name="btnDelete" Content="Delete"  Margin="2" Width="72" Height="28" FontSize="12"/>
                <Button x:Name="btnFormat" Content="Format"  Margin="2" Width="72" Height="28" FontSize="12"/>
                <Button x:Name="btnResize" Content="Resize"  Margin="2" Width="72" Height="28" FontSize="12"/>
                <Button x:Name="btnExtend" Content="Extend"  Margin="2" Width="72" Height="28" FontSize="12"/>
                <Button x:Name="btnSplit"  Content="Split"   Margin="2" Width="72" Height="28" FontSize="12"/>
                <Button x:Name="btnLetter" Content="Letter"  Margin="2" Width="72" Height="28" FontSize="12"/>
                <Button x:Name="btnActive" Content="Active"  Margin="2" Width="72" Height="28" FontSize="12"/>
                <Button x:Name="btnHide"   Content="Hide"    Margin="2" Width="72" Height="28" FontSize="12"/>
              </WrapPanel>
            </Border>
            <Border BorderBrush="#CCC" BorderThickness="1" CornerRadius="4"
                    Background="White" Margin="0,0,0,0">
              <ListView x:Name="lstPartitions" BorderThickness="0" Background="Transparent"
                        SelectionMode="Single" FontSize="13">
                <ListView.View>
                  <GridView>
                    <GridViewColumn Header="#"      Width="30"  DisplayMemberBinding="{Binding Num}"/>
                    <GridViewColumn Header="Letter" Width="50"  DisplayMemberBinding="{Binding Letter}"/>
                    <GridViewColumn Header="Label"  Width="90"  DisplayMemberBinding="{Binding Label}"/>
                    <GridViewColumn Header="Size"   Width="85"  DisplayMemberBinding="{Binding SizeText}"/>
                    <GridViewColumn Header="Free"   Width="85"  DisplayMemberBinding="{Binding FreeText}"/>
                    <GridViewColumn Header="Type"   Width="72"  DisplayMemberBinding="{Binding Type}"/>
                    <GridViewColumn Header="FS"     Width="55"  DisplayMemberBinding="{Binding FileSystem}"/>
                    <GridViewColumn Header="Details" Width="200" DisplayMemberBinding="{Binding Details}"/>
                  </GridView>
                </ListView.View>
              </ListView>
            </Border>
          </DockPanel>
        </TabItem>

        <!-- ═══ TAB 2: HEALTH ═══ -->
        <TabItem Header="Disk Health">
          <DockPanel Margin="8,4,0,0">
            <StackPanel DockPanel.Dock="Top" Orientation="Horizontal" Margin="0,0,0,10">
              <TextBlock Text="Physical Disk:" VerticalAlignment="Center" FontWeight="SemiBold" Margin="0,0,8,0"/>
              <ComboBox x:Name="cmbHealthDisk" Width="520" Height="28" FontSize="13"/>
              <Button x:Name="btnRefreshHealth" Content="Refresh" Width="75" Height="28"
                      Margin="8,0,0,0" FontSize="12"/>
            </StackPanel>
            <ScrollViewer VerticalScrollBarVisibility="Auto">
              <StackPanel x:Name="pnlHealth" Margin="0,0,8,8"/>
            </ScrollViewer>
          </DockPanel>
        </TabItem>

        <!-- ═══ TAB 3: TOOLS ═══ -->
        <TabItem Header="Tools">
          <ScrollViewer VerticalScrollBarVisibility="Auto" Margin="8,4,0,0">
            <StackPanel x:Name="pnlTools" Margin="0,0,8,8"/>
          </ScrollViewer>
        </TabItem>

        <!-- ═══ TAB 4: DISK IMAGES ═══ -->
        <TabItem Header="Disk Images">
          <ScrollViewer VerticalScrollBarVisibility="Auto" Margin="8,4,0,0">
            <StackPanel x:Name="pnlImages" Margin="0,0,8,8"/>
          </ScrollViewer>
        </TabItem>
      </TabControl>
    </DockPanel>
  </DockPanel>
</Window>
'@

$reader = [System.Xml.XmlNodeReader]::new($xaml)
$window = [System.Windows.Markup.XamlReader]::Load($reader)

# ── Control references ──────────────────────────────────────────────────────────

$txtStatus     = $window.FindName('txtStatus')
$txtLog        = $window.FindName('txtLog')
$tabMain       = $window.FindName('tabMain')
$cmbDisk       = $window.FindName('cmbDisk')
$btnRefresh    = $window.FindName('btnRefresh')
$grdDiskBar    = $window.FindName('grdDiskBar')
$lstPartitions = $window.FindName('lstPartitions')
$btnCreate     = $window.FindName('btnCreate')
$btnDelete     = $window.FindName('btnDelete')
$btnFormat     = $window.FindName('btnFormat')
$btnResize     = $window.FindName('btnResize')
$btnExtend     = $window.FindName('btnExtend')
$btnSplit      = $window.FindName('btnSplit')
$btnLetter     = $window.FindName('btnLetter')
$btnActive     = $window.FindName('btnActive')
$btnHide       = $window.FindName('btnHide')
$cmbHealthDisk = $window.FindName('cmbHealthDisk')
$btnRefreshHealth = $window.FindName('btnRefreshHealth')
$pnlHealth     = $window.FindName('pnlHealth')
$pnlTools      = $window.FindName('pnlTools')
$pnlImages     = $window.FindName('pnlImages')

# ── State ───────────────────────────────────────────────────────────────────────

$script:allDisks     = @()
$script:partitions   = @()
$script:selectedDisk = -1
$script:physDisks    = @()
$script:brushConv    = [System.Windows.Media.BrushConverter]::new()

# ── Utilities ───────────────────────────────────────────────────────────────────

function Format-DiskSize ([long]$Bytes) {
    if ($Bytes -ge 1TB) { return "$([math]::Round($Bytes / 1TB, 2)) TB" }
    if ($Bytes -ge 1GB) { return "$([math]::Round($Bytes / 1GB, 2)) GB" }
    if ($Bytes -ge 1MB) { return "$([math]::Round($Bytes / 1MB, 0)) MB" }
    return "$([math]::Round($Bytes / 1KB, 0)) KB"
}

function Write-UILog ([string]$Msg) {
    $stamp = Get-Date -Format 'HH:mm:ss'
    $txtLog.AppendText("[$stamp] $Msg`r`n")
    $txtLog.ScrollToEnd()
    $window.Dispatcher.Invoke([System.Windows.Threading.DispatcherPriority]::Background, [action]{})
}

function Get-PartColor ([string]$Type) {
    switch ($Type) {
        'System'      { '#4682B4' }
        'Reserved'    { '#708090' }
        'Recovery'    { '#E67E22' }
        'Basic'       { '#27AE60' }
        'Unallocated' { '#3D4F5F' }
        default       { '#8E44AD' }
    }
}

function Get-AvailableLetters {
    $used = @(Get-Partition -ErrorAction SilentlyContinue | Where-Object DriveLetter |
              ForEach-Object { $_.DriveLetter })
    $all = [char[]]('D'..'Z')
    return @($all | Where-Object { $_ -notin $used })
}

function Show-FormDialog {
    param([string]$Title, [array]$Fields, [int]$Width = 420)

    $dlg = [System.Windows.Window]::new()
    $dlg.Title = $Title
    $dlg.Width = $Width
    $dlg.SizeToContent = 'Height'
    $dlg.WindowStartupLocation = 'CenterOwner'
    $dlg.Owner = $window
    $dlg.ResizeMode = 'NoResize'
    $dlg.Background = $script:brushConv.ConvertFrom('#F0F2F5')

    $sp = [System.Windows.Controls.StackPanel]::new()
    $sp.Margin = [System.Windows.Thickness]::new(16, 12, 16, 16)
    $controls = @{}

    foreach ($f in $Fields) {
        if ($f.Type -eq 'CheckBox') {
            $chk = [System.Windows.Controls.CheckBox]::new()
            $chk.Content = $f.Label
            $chk.IsChecked = [bool]$f.Default
            $chk.FontSize = 13
            $chk.Margin = [System.Windows.Thickness]::new(0, 8, 0, 0)
            $sp.Children.Add($chk) | Out-Null
            $controls[$f.Name] = $chk
            continue
        }
        $lbl = [System.Windows.Controls.TextBlock]::new()
        $lbl.Text = $f.Label
        $lbl.FontWeight = [System.Windows.FontWeights]::SemiBold
        $lbl.Margin = [System.Windows.Thickness]::new(0, 10, 0, 4)
        $lbl.FontSize = 12
        $sp.Children.Add($lbl) | Out-Null

        switch ($f.Type) {
            'TextBox' {
                $tb = [System.Windows.Controls.TextBox]::new()
                $tb.Text = "$($f.Default)"
                $tb.Height = 28; $tb.FontSize = 13; $tb.Padding = [System.Windows.Thickness]::new(4,2,4,2)
                $sp.Children.Add($tb) | Out-Null
                $controls[$f.Name] = $tb
            }
            'ComboBox' {
                $cb = [System.Windows.Controls.ComboBox]::new()
                foreach ($opt in $f.Options) { $cb.Items.Add($opt) | Out-Null }
                if ($f.Default -and $cb.Items.Contains($f.Default)) { $cb.SelectedItem = $f.Default }
                elseif ($cb.Items.Count -gt 0) { $cb.SelectedIndex = 0 }
                $cb.Height = 28; $cb.FontSize = 13
                $sp.Children.Add($cb) | Out-Null
                $controls[$f.Name] = $cb
            }
            'Info' {
                $info = [System.Windows.Controls.TextBlock]::new()
                $info.Text = "$($f.Default)"
                $info.FontSize = 13; $info.Foreground = $script:brushConv.ConvertFrom('#0078D4')
                $sp.Children.Add($info) | Out-Null
                $controls[$f.Name] = $info
            }
        }
    }

    $bp = [System.Windows.Controls.StackPanel]::new()
    $bp.Orientation = 'Horizontal'
    $bp.HorizontalAlignment = 'Right'
    $bp.Margin = [System.Windows.Thickness]::new(0, 16, 0, 0)

    $ok = [System.Windows.Controls.Button]::new()
    $ok.Content = 'OK'; $ok.Width = 90; $ok.Height = 32; $ok.IsDefault = $true
    $ok.FontWeight = [System.Windows.FontWeights]::SemiBold
    $ok.Background = $script:brushConv.ConvertFrom('#0078D4')
    $ok.Foreground = [System.Windows.Media.Brushes]::White
    $ok.Add_Click({ $dlg.DialogResult = $true })
    $bp.Children.Add($ok) | Out-Null

    $cancel = [System.Windows.Controls.Button]::new()
    $cancel.Content = 'Cancel'; $cancel.Width = 90; $cancel.Height = 32
    $cancel.Margin = [System.Windows.Thickness]::new(8, 0, 0, 0); $cancel.IsCancel = $true
    $bp.Children.Add($cancel) | Out-Null

    $sp.Children.Add($bp) | Out-Null
    $dlg.Content = $sp

    if ($dlg.ShowDialog()) {
        $result = @{}
        foreach ($key in $controls.Keys) {
            $c = $controls[$key]
            if ($c -is [System.Windows.Controls.TextBox])  { $result[$key] = $c.Text }
            elseif ($c -is [System.Windows.Controls.ComboBox]) { $result[$key] = $c.SelectedItem }
            elseif ($c -is [System.Windows.Controls.CheckBox]) { $result[$key] = $c.IsChecked }
            elseif ($c -is [System.Windows.Controls.TextBlock]) { $result[$key] = $c.Text }
        }
        return $result
    }
    return $null
}

function Get-SelectedPartition {
    $sel = $lstPartitions.SelectedItem
    if (-not $sel) {
        [System.Windows.MessageBox]::Show('Select a partition first.', 'PartitionPilot', 'OK', 'Information')
        return $null
    }
    $pn = $sel.Num
    return $script:partitions | Where-Object PartitionNumber -eq $pn
}

# ── Data loading ────────────────────────────────────────────────────────────────

function Load-Disks {
    $script:allDisks = @(Get-Disk | Where-Object OperationalStatus -eq 'Online')
    $cmbDisk.Items.Clear()
    foreach ($d in $script:allDisks) {
        $cmbDisk.Items.Add("Disk $($d.Number): $($d.FriendlyName)  ($(Format-DiskSize $d.Size), $($d.PartitionStyle))") | Out-Null
    }
    if ($cmbDisk.Items.Count -gt 0) { $cmbDisk.SelectedIndex = 0 }
}

function Load-Partitions ([int]$DiskNum) {
    $script:selectedDisk = $DiskNum
    $script:partitions = @(Get-Partition -DiskNumber $DiskNum -ErrorAction SilentlyContinue | Sort-Object Offset)

    $lstPartitions.Items.Clear()
    foreach ($p in $script:partitions) {
        $vol    = Get-Volume -Partition $p -ErrorAction SilentlyContinue
        $fs     = if ($vol) { $vol.FileSystemType } else { '' }
        $label  = if ($vol) { $vol.FileSystemLabel } else { '' }
        $letter = if ($p.DriveLetter) { "$($p.DriveLetter):" } else { '' }
        $free   = if ($vol -and $vol.SizeRemaining -gt 0) { Format-DiskSize $vol.SizeRemaining } else { '' }

        $details = @()
        if ($p.IsBoot)   { $details += 'Boot' }
        if ($p.IsSystem) { $details += 'System' }
        if ($p.IsActive) { $details += 'Active' }
        if ($p.IsHidden) { $details += 'Hidden' }
        if ($letter) {
            $pf = Get-CimInstance Win32_PageFileUsage -ErrorAction SilentlyContinue |
                  Where-Object { $_.Name -like "$letter*" }
            if ($pf) { $details += "Pagefile" }
        }

        $lstPartitions.Items.Add([PSCustomObject]@{
            Num      = $p.PartitionNumber
            Letter   = $letter
            Label    = $label
            SizeText = Format-DiskSize $p.Size
            FreeText = $free
            Type     = $p.Type
            FileSystem = $fs
            Details  = ($details -join ', ')
        }) | Out-Null
    }

    $disk = Get-Disk -Number $DiskNum
    $txtStatus.Text = "Disk $($DiskNum): $($disk.FriendlyName) | $(Format-DiskSize $disk.Size) | $($disk.PartitionStyle) | $($script:partitions.Count) partition(s)"
    Draw-DiskBar $DiskNum
}

# ── Disk bar visualization ──────────────────────────────────────────────────────

function Draw-DiskBar ([int]$DiskNum) {
    $grdDiskBar.Children.Clear()
    $grdDiskBar.ColumnDefinitions.Clear()

    $disk      = Get-Disk -Number $DiskNum
    $totalSize = $disk.Size
    $parts     = @(Get-Partition -DiskNumber $DiskNum -ErrorAction SilentlyContinue | Sort-Object Offset)

    $segments = [System.Collections.ArrayList]::new()
    $cursor   = 0

    foreach ($p in $parts) {
        if ($p.Offset -gt ($cursor + 1MB)) {
            $gap = $p.Offset - $cursor
            $segments.Add([PSCustomObject]@{ Type='Unallocated'; Size=$gap;
                Label="Free`n$(Format-DiskSize $gap)"; Color=(Get-PartColor 'Unallocated') }) | Out-Null
        }
        $lt = if ($p.DriveLetter) { "$($p.DriveLetter): " } else { '' }
        $segments.Add([PSCustomObject]@{ Type=$p.Type; Size=$p.Size;
            Label="$lt$($p.Type)`n$(Format-DiskSize $p.Size)"; Color=(Get-PartColor $p.Type) }) | Out-Null
        $cursor = $p.Offset + $p.Size
    }
    if ($cursor -lt ($totalSize - 1MB)) {
        $trail = $totalSize - $cursor
        $segments.Add([PSCustomObject]@{ Type='Unallocated'; Size=$trail;
            Label="Free`n$(Format-DiskSize $trail)"; Color=(Get-PartColor 'Unallocated') }) | Out-Null
    }

    $col = 0
    foreach ($seg in $segments) {
        $prop = [Math]::Max($seg.Size / $totalSize, 0.018)
        $cd = [System.Windows.Controls.ColumnDefinition]::new()
        $cd.Width = [System.Windows.GridLength]::new($prop, 'Star')
        $grdDiskBar.ColumnDefinitions.Add($cd)
        $bdr = [System.Windows.Controls.Border]::new()
        $bdr.Background  = $script:brushConv.ConvertFrom($seg.Color)
        $bdr.Margin       = [System.Windows.Thickness]::new(1, 5, 1, 5)
        $bdr.CornerRadius = [System.Windows.CornerRadius]::new(4)
        $tb = [System.Windows.Controls.TextBlock]::new()
        $tb.Text = $seg.Label; $tb.Foreground = [System.Windows.Media.Brushes]::White
        $tb.FontSize = 10; $tb.FontWeight = [System.Windows.FontWeights]::SemiBold
        $tb.HorizontalAlignment = 'Center'; $tb.VerticalAlignment = 'Center'
        $tb.TextAlignment = 'Center'; $tb.TextTrimming = 'CharacterEllipsis'
        $tb.Margin = [System.Windows.Thickness]::new(2,0,2,0)
        $bdr.Child = $tb
        [System.Windows.Controls.Grid]::SetColumn($bdr, $col)
        $grdDiskBar.Children.Add($bdr) | Out-Null
        $col++
    }
}

# ── Partition Operations ────────────────────────────────────────────────────────

function Invoke-CreatePartition {
    $disk = Get-Disk -Number $script:selectedDisk
    $freeBytes = $disk.LargestFreeExtent
    if ($freeBytes -lt 1MB) {
        [System.Windows.MessageBox]::Show('No unallocated space on this disk.', 'Create Partition', 'OK', 'Warning')
        return
    }
    $freeGB = [math]::Round($freeBytes / 1GB, 2)
    $letters = Get-AvailableLetters
    $r = Show-FormDialog -Title "Create Partition" -Fields @(
        @{Name='Info';   Label='Available space:'; Type='Info'; Default="$freeGB GB"}
        @{Name='SizeGB'; Label='Size (GB):';       Type='TextBox'; Default=$freeGB}
        @{Name='Letter'; Label='Drive Letter:';    Type='ComboBox'; Options=$letters}
        @{Name='FS';     Label='File System:';     Type='ComboBox'; Options=@('NTFS','FAT32','exFAT','ReFS'); Default='NTFS'}
        @{Name='Label';  Label='Volume Label:';    Type='TextBox'; Default=''}
        @{Name='Quick';  Label='Quick Format';     Type='CheckBox'; Default=$true}
    )
    if (-not $r) { return }
    try {
        $sizeBytes = [math]::Round([double]$r.SizeGB * 1GB)
        if ($sizeBytes -gt $freeBytes) { $sizeBytes = $freeBytes }
        Write-UILog "Creating partition: $(Format-DiskSize $sizeBytes), $($r.FS), letter $($r.Letter)..."
        $newPart = New-Partition -DiskNumber $script:selectedDisk -Size $sizeBytes -DriveLetter ([char]$r.Letter)
        $formatParams = @{ DriveLetter = [char]$r.Letter; FileSystem = $r.FS; Confirm = $false }
        if ($r.Label) { $formatParams.NewFileSystemLabel = $r.Label }
        if (-not $r.Quick) { $formatParams.Full = $true }
        Format-Volume @formatParams | Out-Null
        Write-UILog "Partition created and formatted successfully."
    } catch {
        Write-UILog "ERROR: $($_.Exception.Message)"
        [System.Windows.MessageBox]::Show($_.Exception.Message, 'Error', 'OK', 'Error')
    }
    Load-Partitions $script:selectedDisk
}

function Invoke-DeletePartition {
    $part = Get-SelectedPartition
    if (-not $part) { return }
    $lt = if ($part.DriveLetter) { "$($part.DriveLetter): " } else { '' }
    $msg = "Delete Partition $($part.PartitionNumber) ($lt$($part.Type), $(Format-DiskSize $part.Size))?`n`nALL DATA WILL BE LOST."
    if ([System.Windows.MessageBox]::Show($msg, 'Confirm Delete', 'YesNo', 'Warning') -ne 'Yes') { return }
    try {
        Write-UILog "Deleting partition $($part.PartitionNumber)..."
        $dpScript = "select disk $($script:selectedDisk)`nselect partition $($part.PartitionNumber)`ndelete partition override"
        $out = $dpScript | diskpart 2>&1
        if (($out | Out-String) -match 'error') { throw "diskpart: $(($out | Out-String))" }
        Write-UILog "Partition deleted."
    } catch {
        Write-UILog "ERROR: $($_.Exception.Message)"
        [System.Windows.MessageBox]::Show($_.Exception.Message, 'Error', 'OK', 'Error')
    }
    Load-Partitions $script:selectedDisk
}

function Invoke-FormatPartition {
    $part = Get-SelectedPartition
    if (-not $part -or -not $part.DriveLetter) {
        [System.Windows.MessageBox]::Show('Select a partition with a drive letter.', 'Format', 'OK', 'Information')
        return
    }
    $vol = Get-Volume -DriveLetter $part.DriveLetter -ErrorAction SilentlyContinue
    $currentFS = if ($vol) { $vol.FileSystemType } else { 'Unknown' }
    $r = Show-FormDialog -Title "Format $($part.DriveLetter):" -Fields @(
        @{Name='Info'; Label='Current:'; Type='Info'; Default="$currentFS, $(Format-DiskSize $part.Size)"}
        @{Name='FS';   Label='File System:'; Type='ComboBox'; Options=@('NTFS','FAT32','exFAT','ReFS'); Default='NTFS'}
        @{Name='Label'; Label='Volume Label:'; Type='TextBox'; Default=''}
        @{Name='Quick'; Label='Quick Format'; Type='CheckBox'; Default=$true}
    )
    if (-not $r) { return }
    $confirm = "Format $($part.DriveLetter): as $($r.FS)?`n`nALL DATA ON THIS VOLUME WILL BE LOST."
    if ([System.Windows.MessageBox]::Show($confirm, 'Confirm Format', 'YesNo', 'Warning') -ne 'Yes') { return }
    try {
        Write-UILog "Formatting $($part.DriveLetter): as $($r.FS)..."
        $fp = @{ DriveLetter = $part.DriveLetter; FileSystem = $r.FS; Force = $true; Confirm = $false }
        if ($r.Label) { $fp.NewFileSystemLabel = $r.Label }
        if (-not $r.Quick) { $fp.Full = $true }
        Format-Volume @fp | Out-Null
        Write-UILog "Format complete."
    } catch {
        Write-UILog "ERROR: $($_.Exception.Message)"
        [System.Windows.MessageBox]::Show($_.Exception.Message, 'Error', 'OK', 'Error')
    }
    Load-Partitions $script:selectedDisk
}

function Invoke-ResizePartition {
    $part = Get-SelectedPartition
    if (-not $part -or -not $part.DriveLetter) {
        [System.Windows.MessageBox]::Show('Select a partition with a drive letter.', 'Resize', 'OK', 'Information')
        return
    }
    $supported = Get-PartitionSupportedSize -DriveLetter $part.DriveLetter
    $minGB = [math]::Round($supported.SizeMin / 1GB, 2)
    $maxGB = [math]::Round($supported.SizeMax / 1GB, 2)
    $curGB = [math]::Round($part.Size / 1GB, 2)
    $r = Show-FormDialog -Title "Resize $($part.DriveLetter):" -Fields @(
        @{Name='Info'; Label='Current Size:'; Type='Info'; Default="$curGB GB  (min: $minGB GB, max: $maxGB GB)"}
        @{Name='SizeGB'; Label='New Size (GB):'; Type='TextBox'; Default=$curGB}
    )
    if (-not $r) { return }
    try {
        $newBytes = [math]::Round([double]$r.SizeGB * 1GB)
        $newBytes = [math]::Max($newBytes, $supported.SizeMin)
        $newBytes = [math]::Min($newBytes, $supported.SizeMax)
        $action = if ($newBytes -lt $part.Size) { 'Shrinking' } else { 'Extending' }
        Write-UILog "$action $($part.DriveLetter): to $(Format-DiskSize $newBytes)..."
        Resize-Partition -DriveLetter $part.DriveLetter -Size $newBytes
        Write-UILog "Resize complete."
    } catch {
        Write-UILog "ERROR: $($_.Exception.Message)"
        [System.Windows.MessageBox]::Show($_.Exception.Message, 'Error', 'OK', 'Error')
    }
    Load-Partitions $script:selectedDisk
}

function Invoke-ExtendPartition {
    $part = Get-SelectedPartition
    if (-not $part -or -not $part.DriveLetter) {
        [System.Windows.MessageBox]::Show('Select a partition with a drive letter.', 'Extend', 'OK', 'Information')
        return
    }
    $disk = Get-Disk -Number $script:selectedDisk
    $cursor = $part.Offset + $part.Size
    $afterParts = @($script:partitions | Where-Object { $_.Offset -ge $cursor } | Sort-Object Offset)
    $toRemove = [System.Collections.ArrayList]::new()
    $gain = [long]0
    $hasRecovery = $false
    $hasPagefile = $false

    foreach ($ap in $afterParts) {
        if ($ap.Offset -gt $cursor) { $gain += ($ap.Offset - $cursor) }
        if ($ap.IsBoot -or $ap.IsSystem) { break }
        if ($ap.DriveLetter) {
            $pf = Get-CimInstance Win32_PageFileUsage -ErrorAction SilentlyContinue |
                  Where-Object { $_.Name -like "$($ap.DriveLetter):*" }
            if ($pf) { $hasPagefile = $true }
        }
        if ($ap.Type -eq 'Recovery') { $hasRecovery = $true }
        $toRemove.Add($ap) | Out-Null
        $gain += $ap.Size
        $cursor = $ap.Offset + $ap.Size
    }
    if ($cursor -lt $disk.Size) { $gain += ($disk.Size - $cursor) }

    if ($gain -le 0) {
        [System.Windows.MessageBox]::Show("No reclaimable space adjacent to $($part.DriveLetter):.", 'Extend', 'OK', 'Information')
        return
    }

    $steps = @("Extend $($part.DriveLetter): by $(Format-DiskSize $gain)")
    if ($toRemove.Count -gt 0) {
        $steps += "Remove $($toRemove.Count) partition(s) to free space"
    }
    if ($hasPagefile) { $steps += "Requires reboot (pagefile in the way)" }
    $msg = ($steps -join "`n") + "`n`nProceed?"
    if ([System.Windows.MessageBox]::Show($msg, 'Extend Partition', 'YesNo', 'Question') -ne 'Yes') { return }

    try {
        if ($hasRecovery) {
            Write-UILog "Disabling Recovery Environment..."
            reagentc /disable 2>&1 | Out-Null
        }
        if ($hasPagefile) {
            Write-UILog "Relocating pagefile..."
            $cs = Get-CimInstance Win32_ComputerSystem
            if ($cs.AutomaticManagedPagefile) { Set-CimInstance $cs -Property @{AutomaticManagedPagefile=$false} }
            foreach ($r in $toRemove) {
                if ($r.DriveLetter) {
                    $pfs = Get-CimInstance Win32_PageFileSetting -ErrorAction SilentlyContinue |
                           Where-Object { $_.Name -like "$($r.DriveLetter):*" }
                    if ($pfs) { Remove-CimInstance $pfs }
                }
            }
            $existing = Get-CimInstance Win32_PageFileSetting -ErrorAction SilentlyContinue |
                        Where-Object { $_.Name -like "$($part.DriveLetter):*" }
            if (-not $existing) {
                try { New-CimInstance -ClassName Win32_PageFileSetting -Property @{Name="$($part.DriveLetter):\pagefile.sys"} -ErrorAction Stop } catch {}
            }
            Write-UILog "Reboot required. Run PartitionPilot again after restart."
            [System.Windows.MessageBox]::Show("Reboot required to release the pagefile.`nRun PartitionPilot again after restart.", 'Reboot Required', 'OK', 'Information')
            Load-Partitions $script:selectedDisk
            return
        }
        foreach ($r in $toRemove) {
            Write-UILog "Deleting partition $($r.PartitionNumber)..."
            $dp = "select disk $($script:selectedDisk)`nselect partition $($r.PartitionNumber)`ndelete partition override"
            $dp | diskpart 2>&1 | Out-Null
        }
        Start-Sleep -Milliseconds 500
        $max = (Get-PartitionSupportedSize -DriveLetter $part.DriveLetter).SizeMax
        Write-UILog "Extending $($part.DriveLetter): to $(Format-DiskSize $max)..."
        Resize-Partition -DriveLetter $part.DriveLetter -Size $max
        Write-UILog "Extended successfully."
        reagentc /enable 2>&1 | Out-Null
        [System.Windows.MessageBox]::Show("$($part.DriveLetter): extended to $(Format-DiskSize $max).", 'Success', 'OK', 'Information')
    } catch {
        Write-UILog "ERROR: $($_.Exception.Message)"
        [System.Windows.MessageBox]::Show($_.Exception.Message, 'Error', 'OK', 'Error')
    }
    Load-Partitions $script:selectedDisk
}

function Invoke-SplitPartition {
    $part = Get-SelectedPartition
    if (-not $part -or -not $part.DriveLetter) {
        [System.Windows.MessageBox]::Show('Select a partition with a drive letter.', 'Split', 'OK', 'Information')
        return
    }
    $supported = Get-PartitionSupportedSize -DriveLetter $part.DriveLetter
    $minGB = [math]::Ceiling($supported.SizeMin / 1GB)
    $curGB = [math]::Round($part.Size / 1GB, 2)
    $maxNewGB = [math]::Floor(($part.Size - $supported.SizeMin) / 1GB)
    if ($maxNewGB -lt 1) {
        [System.Windows.MessageBox]::Show('Not enough space to split this partition.', 'Split', 'OK', 'Warning')
        return
    }
    $letters = Get-AvailableLetters
    $r = Show-FormDialog -Title "Split $($part.DriveLetter):" -Fields @(
        @{Name='Info'; Label="Current size: $curGB GB"; Type='Info'; Default="Min keep: $minGB GB, Max new: $maxNewGB GB"}
        @{Name='NewGB'; Label='New partition size (GB):'; Type='TextBox'; Default=[math]::Floor($maxNewGB / 2)}
        @{Name='Letter'; Label='New drive letter:'; Type='ComboBox'; Options=$letters}
        @{Name='FS'; Label='New file system:'; Type='ComboBox'; Options=@('NTFS','FAT32','exFAT','ReFS'); Default='NTFS'}
        @{Name='Label'; Label='New volume label:'; Type='TextBox'; Default=''}
    )
    if (-not $r) { return }
    try {
        $newBytes = [math]::Round([double]$r.NewGB * 1GB)
        $shrinkTo = $part.Size - $newBytes
        if ($shrinkTo -lt $supported.SizeMin) { throw "Cannot shrink below $(Format-DiskSize $supported.SizeMin)." }
        Write-UILog "Shrinking $($part.DriveLetter): to $(Format-DiskSize $shrinkTo)..."
        Resize-Partition -DriveLetter $part.DriveLetter -Size $shrinkTo
        Write-UILog "Creating new partition..."
        $np = New-Partition -DiskNumber $script:selectedDisk -Size $newBytes -DriveLetter ([char]$r.Letter)
        $fp = @{ DriveLetter = [char]$r.Letter; FileSystem = $r.FS; Confirm = $false }
        if ($r.Label) { $fp.NewFileSystemLabel = $r.Label }
        Format-Volume @fp | Out-Null
        Write-UILog "Split complete. New partition: $($r.Letter): ($(Format-DiskSize $newBytes), $($r.FS))."
    } catch {
        Write-UILog "ERROR: $($_.Exception.Message)"
        [System.Windows.MessageBox]::Show($_.Exception.Message, 'Error', 'OK', 'Error')
    }
    Load-Partitions $script:selectedDisk
}

function Invoke-ChangeLetter {
    $part = Get-SelectedPartition
    if (-not $part) { return }
    $letters = Get-AvailableLetters
    $currentLetter = if ($part.DriveLetter) { "$($part.DriveLetter):" } else { '(none)' }
    $r = Show-FormDialog -Title "Change Drive Letter" -Fields @(
        @{Name='Info'; Label='Current letter:'; Type='Info'; Default=$currentLetter}
        @{Name='Letter'; Label='New letter:'; Type='ComboBox'; Options=$letters}
    )
    if (-not $r) { return }
    try {
        Write-UILog "Changing drive letter to $($r.Letter):..."
        if ($part.DriveLetter) {
            Set-Partition -DiskNumber $script:selectedDisk -PartitionNumber $part.PartitionNumber -NewDriveLetter ([char]$r.Letter)
        } else {
            Add-PartitionAccessPath -DiskNumber $script:selectedDisk -PartitionNumber $part.PartitionNumber -AssignDriveLetter
            Set-Partition -DiskNumber $script:selectedDisk -PartitionNumber $part.PartitionNumber -NewDriveLetter ([char]$r.Letter)
        }
        Write-UILog "Drive letter changed to $($r.Letter):."
    } catch {
        Write-UILog "ERROR: $($_.Exception.Message)"
        [System.Windows.MessageBox]::Show($_.Exception.Message, 'Error', 'OK', 'Error')
    }
    Load-Partitions $script:selectedDisk
}

function Invoke-SetActive {
    $part = Get-SelectedPartition
    if (-not $part) { return }
    $disk = Get-Disk -Number $script:selectedDisk
    if ($disk.PartitionStyle -ne 'MBR') {
        [System.Windows.MessageBox]::Show('Active flag is only for MBR disks.', 'Set Active', 'OK', 'Information')
        return
    }
    $action = if ($part.IsActive) { 'remove active flag from' } else { 'set as active' }
    if ([System.Windows.MessageBox]::Show("$action partition $($part.PartitionNumber)?", 'Set Active', 'YesNo', 'Question') -ne 'Yes') { return }
    try {
        Set-Partition -DiskNumber $script:selectedDisk -PartitionNumber $part.PartitionNumber -IsActive (-not $part.IsActive)
        Write-UILog "Active flag updated for partition $($part.PartitionNumber)."
    } catch {
        Write-UILog "ERROR: $($_.Exception.Message)"
        [System.Windows.MessageBox]::Show($_.Exception.Message, 'Error', 'OK', 'Error')
    }
    Load-Partitions $script:selectedDisk
}

function Invoke-ToggleHide {
    $part = Get-SelectedPartition
    if (-not $part) { return }
    try {
        if ($part.DriveLetter) {
            Write-UILog "Hiding partition $($part.PartitionNumber) (removing $($part.DriveLetter):)..."
            Remove-PartitionAccessPath -DiskNumber $script:selectedDisk -PartitionNumber $part.PartitionNumber -AccessPath "$($part.DriveLetter):\"
            Write-UILog "Partition hidden."
        } else {
            $letters = Get-AvailableLetters
            if ($letters.Count -eq 0) { throw "No drive letters available." }
            Write-UILog "Unhiding partition $($part.PartitionNumber)..."
            Add-PartitionAccessPath -DiskNumber $script:selectedDisk -PartitionNumber $part.PartitionNumber -AssignDriveLetter
            Write-UILog "Partition unhidden."
        }
    } catch {
        Write-UILog "ERROR: $($_.Exception.Message)"
        [System.Windows.MessageBox]::Show($_.Exception.Message, 'Error', 'OK', 'Error')
    }
    Load-Partitions $script:selectedDisk
}

# ── Tab 2: Health Dashboard ─────────────────────────────────────────────────────

function New-InfoCard ([string]$Title, [hashtable]$Data) {
    $gb = [System.Windows.Controls.GroupBox]::new()
    $gb.Header = $Title
    $gb.Margin = [System.Windows.Thickness]::new(0, 0, 0, 10)
    $gb.Padding = [System.Windows.Thickness]::new(10, 6, 10, 6)
    $gb.Background = [System.Windows.Media.Brushes]::White
    $grid = [System.Windows.Controls.Grid]::new()
    $grid.ColumnDefinitions.Add(([System.Windows.Controls.ColumnDefinition]::new()))
    $grid.ColumnDefinitions.Add(([System.Windows.Controls.ColumnDefinition]::new()))
    $grid.ColumnDefinitions[0].Width = [System.Windows.GridLength]::new(170)
    $row = 0
    foreach ($key in $Data.Keys) {
        $grid.RowDefinitions.Add(([System.Windows.Controls.RowDefinition]::new()))
        $lbl = [System.Windows.Controls.TextBlock]::new()
        $lbl.Text = $key; $lbl.FontWeight = [System.Windows.FontWeights]::SemiBold
        $lbl.Margin = [System.Windows.Thickness]::new(0,3,0,3); $lbl.FontSize = 12.5
        [System.Windows.Controls.Grid]::SetRow($lbl, $row)
        [System.Windows.Controls.Grid]::SetColumn($lbl, 0)
        $grid.Children.Add($lbl) | Out-Null
        $val = [System.Windows.Controls.TextBlock]::new()
        $val.Text = "$($Data[$key])"; $val.FontSize = 12.5
        $val.Margin = [System.Windows.Thickness]::new(0,3,0,3)
        [System.Windows.Controls.Grid]::SetRow($val, $row)
        [System.Windows.Controls.Grid]::SetColumn($val, 1)
        $grid.Children.Add($val) | Out-Null
        $row++
    }
    $gb.Content = $grid
    return $gb
}

function Load-HealthData {
    $pnlHealth.Children.Clear()
    $idx = $cmbHealthDisk.SelectedIndex
    if ($idx -lt 0) { return }
    $pd = $script:physDisks[$idx]

    $mediaType = switch ($pd.MediaType) { 3 {'HDD'}; 4 {'SSD'}; default {"$($pd.MediaType)"} }
    $busType = switch ($pd.BusType) {
        1 {'SCSI'}; 2 {'ATAPI'}; 3 {'ATA'}; 5 {'USB'}; 6 {'SAS'}
        7 {'SATA'}; 8 {'SD'}; 9 {'MMC'}; 11 {'SATA'}; 17 {'NVMe'}; default {"$($pd.BusType)"}
    }

    $diskInfo = [ordered]@{
        'Model'           = $pd.FriendlyName
        'Serial Number'   = $pd.SerialNumber
        'Firmware'        = $pd.FirmwareVersion
        'Media Type'      = $mediaType
        'Bus Type'        = $busType
        'Capacity'        = Format-DiskSize $pd.Size
        'Logical Sector'  = "$($pd.LogicalSectorSize) bytes"
        'Physical Sector' = "$($pd.PhysicalSectorSize) bytes"
        'Health'          = "$($pd.HealthStatus)"
        'Status'          = "$($pd.OperationalStatus)"
    }
    $pnlHealth.Children.Add((New-InfoCard 'Disk Information' $diskInfo)) | Out-Null

    $smart = Get-StorageReliabilityCounter -PhysicalDisk $pd -ErrorAction SilentlyContinue
    if ($smart) {
        $healthData = [ordered]@{}
        if ($null -ne $smart.Temperature)            { $healthData['Temperature']    = "$($smart.Temperature) C" }
        if ($null -ne $smart.Wear)                   { $healthData['Wear Level']     = "$($smart.Wear)% used" }
        if ($null -ne $smart.PowerOnHours -and $smart.PowerOnHours -gt 0) {
            $days = [math]::Round($smart.PowerOnHours / 24, 1)
            $healthData['Power-On Time'] = "$($smart.PowerOnHours) hours ($days days)"
        }
        if ($null -ne $smart.ReadErrorsTotal)        { $healthData['Read Errors']    = "$($smart.ReadErrorsTotal) ($($smart.ReadErrorsCorrected) corrected)" }
        if ($null -ne $smart.WriteErrorsTotal)       { $healthData['Write Errors']   = "$($smart.WriteErrorsTotal) ($($smart.WriteErrorsCorrected) corrected)" }
        if ($null -ne $smart.ReadLatencyMax -and $smart.ReadLatencyMax -gt 0) {
            $healthData['Max Read Latency']  = "$($smart.ReadLatencyMax) ns" }
        if ($null -ne $smart.WriteLatencyMax -and $smart.WriteLatencyMax -gt 0) {
            $healthData['Max Write Latency'] = "$($smart.WriteLatencyMax) ns" }
        if ($healthData.Count -gt 0) {
            $pnlHealth.Children.Add((New-InfoCard 'SMART / Reliability Data' $healthData)) | Out-Null
        }
    } else {
        $noSmart = [System.Windows.Controls.TextBlock]::new()
        $noSmart.Text = "SMART data not available for this disk."
        $noSmart.Foreground = $script:brushConv.ConvertFrom('#888')
        $noSmart.Margin = [System.Windows.Thickness]::new(0, 0, 0, 10)
        $pnlHealth.Children.Add($noSmart) | Out-Null
    }

    # 4K Alignment Audit
    $alignGb = [System.Windows.Controls.GroupBox]::new()
    $alignGb.Header = "4K Alignment Audit"
    $alignGb.Margin = [System.Windows.Thickness]::new(0, 0, 0, 10)
    $alignGb.Padding = [System.Windows.Thickness]::new(10)
    $alignGb.Background = [System.Windows.Media.Brushes]::White

    $alignList = [System.Windows.Controls.ListView]::new()
    $alignList.BorderThickness = [System.Windows.Thickness]::new(0)
    $alignList.FontSize = 12.5
    $gv = [System.Windows.Controls.GridView]::new()
    foreach ($h in @(@('Partition',70), @('Letter',55), @('Offset',120), @('Aligned',65), @('Status',180))) {
        $col = [System.Windows.Controls.GridViewColumn]::new()
        $col.Header = $h[0]; $col.Width = $h[1]
        $col.DisplayMemberBinding = [System.Windows.Data.Binding]::new($h[0])
        $gv.Columns.Add($col)
    }
    $alignList.View = $gv

    $allParts = Get-Partition -ErrorAction SilentlyContinue | Sort-Object DiskNumber, Offset
    foreach ($p in $allParts) {
        $aligned = ($p.Offset % 4096) -eq 0
        $status = if ($aligned) { 'OK' } else { "Misaligned (offset mod 4096 = $($p.Offset % 4096))" }
        $alignList.Items.Add([PSCustomObject]@{
            Partition = "#$($p.PartitionNumber) (Disk $($p.DiskNumber))"
            Letter    = if ($p.DriveLetter) { "$($p.DriveLetter):" } else { '' }
            Offset    = "$($p.Offset) bytes"
            Aligned   = if ($aligned) { 'Yes' } else { 'No' }
            Status    = $status
        }) | Out-Null
    }
    $alignGb.Content = $alignList
    $pnlHealth.Children.Add($alignGb) | Out-Null
}

function Load-PhysicalDisks {
    $script:physDisks = @(Get-PhysicalDisk -ErrorAction SilentlyContinue)
    $cmbHealthDisk.Items.Clear()
    foreach ($pd in $script:physDisks) {
        $cmbHealthDisk.Items.Add("$($pd.DeviceId): $($pd.FriendlyName) ($(Format-DiskSize $pd.Size))") | Out-Null
    }
    if ($cmbHealthDisk.Items.Count -gt 0) { $cmbHealthDisk.SelectedIndex = 0 }
}

# ── Tab 3: Tools ────────────────────────────────────────────────────────────────

function New-ToolSection ([string]$Header, [string]$Desc, [scriptblock]$Builder) {
    $gb = [System.Windows.Controls.GroupBox]::new()
    $gb.Header = $Header
    $gb.Margin = [System.Windows.Thickness]::new(0, 0, 0, 10)
    $gb.Padding = [System.Windows.Thickness]::new(10)
    $gb.Background = [System.Windows.Media.Brushes]::White
    $sp = [System.Windows.Controls.StackPanel]::new()
    $d = [System.Windows.Controls.TextBlock]::new()
    $d.Text = $Desc; $d.TextWrapping = 'Wrap'
    $d.Foreground = $script:brushConv.ConvertFrom('#666')
    $d.Margin = [System.Windows.Thickness]::new(0, 0, 0, 8); $d.FontSize = 12
    $sp.Children.Add($d) | Out-Null
    & $Builder $sp
    $gb.Content = $sp
    return $gb
}

function New-ToolRow ([System.Windows.Controls.StackPanel]$Parent, [string]$Label, [System.Windows.UIElement]$Control) {
    $row = [System.Windows.Controls.StackPanel]::new()
    $row.Orientation = 'Horizontal'
    $row.Margin = [System.Windows.Thickness]::new(0, 0, 0, 6)
    $lbl = [System.Windows.Controls.TextBlock]::new()
    $lbl.Text = $Label; $lbl.Width = 80; $lbl.VerticalAlignment = 'Center'; $lbl.FontSize = 12.5
    $row.Children.Add($lbl) | Out-Null
    $row.Children.Add($Control) | Out-Null
    $Parent.Children.Add($row) | Out-Null
}

function New-ActionButton ([string]$Text, [scriptblock]$Action) {
    $btn = [System.Windows.Controls.Button]::new()
    $btn.Content = $Text; $btn.Width = 100; $btn.Height = 30; $btn.Margin = [System.Windows.Thickness]::new(2)
    $btn.FontSize = 12; $btn.Cursor = [System.Windows.Input.Cursors]::Hand
    $btn.Add_Click($Action)
    return $btn
}

function Build-ToolsPage {
    $pnlTools.Children.Clear()

    # ── MBR → GPT ──
    $mbrDisks = @($script:allDisks | Where-Object PartitionStyle -eq 'MBR')
    $pnlTools.Children.Add((New-ToolSection 'Convert MBR to GPT' 'Non-destructive conversion via mbr2gpt.exe. Preserves all data. Only works on system disks with 3 or fewer primary partitions.' {
        param($sp)
        $cmb = [System.Windows.Controls.ComboBox]::new()
        $cmb.Width = 350; $cmb.Height = 28; $cmb.FontSize = 13; $cmb.Tag = 'mbrDiskCmb'
        foreach ($d in $mbrDisks) { $cmb.Items.Add("Disk $($d.Number): $($d.FriendlyName)") | Out-Null }
        if ($cmb.Items.Count -eq 0) { $cmb.Items.Add('(No MBR disks found)') | Out-Null; $cmb.IsEnabled = $false }
        $cmb.SelectedIndex = 0
        New-ToolRow $sp 'Disk:' $cmb
        $bp = [System.Windows.Controls.WrapPanel]::new()
        $bp.Children.Add((New-ActionButton 'Validate' {
            if ($mbrDisks.Count -eq 0) { return }
            $dn = $mbrDisks[$cmb.SelectedIndex].Number
            Write-UILog "Validating MBR to GPT conversion for Disk $dn..."
            $out = mbr2gpt /validate /disk:$dn /allowFullOS 2>&1
            Write-UILog ($out -join "`n")
        })) | Out-Null
        $bp.Children.Add((New-ActionButton 'Convert' {
            if ($mbrDisks.Count -eq 0) { return }
            $dn = $mbrDisks[$cmb.SelectedIndex].Number
            if ([System.Windows.MessageBox]::Show("Convert Disk $dn from MBR to GPT?`nData is preserved but this cannot be undone.", 'Confirm', 'YesNo', 'Warning') -ne 'Yes') { return }
            Write-UILog "Converting Disk $dn to GPT..."
            $out = mbr2gpt /convert /disk:$dn /allowFullOS 2>&1
            Write-UILog ($out -join "`n")
            Load-Disks
        })) | Out-Null
        $sp.Children.Add($bp) | Out-Null
    })) | Out-Null

    # ── FAT32 → NTFS ──
    $fatVols = @(Get-Volume -ErrorAction SilentlyContinue | Where-Object { $_.FileSystem -eq 'FAT32' -and $_.DriveLetter })
    $pnlTools.Children.Add((New-ToolSection 'Convert FAT32 to NTFS' 'One-way, non-destructive conversion. Data is preserved. Cannot be reversed without reformatting.' {
        param($sp)
        $cmb = [System.Windows.Controls.ComboBox]::new()
        $cmb.Width = 350; $cmb.Height = 28; $cmb.FontSize = 13
        foreach ($v in $fatVols) { $cmb.Items.Add("$($v.DriveLetter): $($v.FileSystemLabel) ($(Format-DiskSize $v.Size))") | Out-Null }
        if ($cmb.Items.Count -eq 0) { $cmb.Items.Add('(No FAT32 volumes found)') | Out-Null; $cmb.IsEnabled = $false }
        $cmb.SelectedIndex = 0
        New-ToolRow $sp 'Volume:' $cmb
        $sp.Children.Add((New-ActionButton 'Convert' {
            if ($fatVols.Count -eq 0) { return }
            $drv = $fatVols[$cmb.SelectedIndex].DriveLetter
            if ([System.Windows.MessageBox]::Show("Convert $drv`: from FAT32 to NTFS?`nThis cannot be reversed.", 'Confirm', 'YesNo', 'Warning') -ne 'Yes') { return }
            Write-UILog "Converting $drv`: to NTFS..."
            $out = cmd /c "convert $drv`: /fs:ntfs /v" 2>&1
            Write-UILog ($out -join "`n")
        })) | Out-Null
    })) | Out-Null

    # ── Filesystem Check ──
    $ntfsVols = @(Get-Volume -ErrorAction SilentlyContinue | Where-Object { $_.DriveLetter -and $_.FileSystemType -in @('NTFS','ReFS') })
    $pnlTools.Children.Add((New-ToolSection 'Filesystem Check and Repair' 'Scan volumes for filesystem errors. SpotFix is fast; OfflineScanAndFix is thorough but dismounts the volume.' {
        param($sp)
        $cmb = [System.Windows.Controls.ComboBox]::new()
        $cmb.Width = 350; $cmb.Height = 28; $cmb.FontSize = 13
        foreach ($v in $ntfsVols) { $cmb.Items.Add("$($v.DriveLetter): $($v.FileSystemLabel) ($($v.FileSystemType))") | Out-Null }
        if ($cmb.Items.Count -gt 0) { $cmb.SelectedIndex = 0 }
        New-ToolRow $sp 'Volume:' $cmb
        $modePanel = [System.Windows.Controls.StackPanel]::new()
        $modePanel.Orientation = 'Horizontal'
        $modePanel.Margin = [System.Windows.Thickness]::new(0, 0, 0, 6)
        $rbScan = [System.Windows.Controls.RadioButton]::new(); $rbScan.Content = 'Scan'; $rbScan.IsChecked = $true
        $rbScan.Margin = [System.Windows.Thickness]::new(0,0,16,0); $rbScan.FontSize = 12.5
        $rbSpot = [System.Windows.Controls.RadioButton]::new(); $rbSpot.Content = 'SpotFix'
        $rbSpot.Margin = [System.Windows.Thickness]::new(0,0,16,0); $rbSpot.FontSize = 12.5
        $rbFull = [System.Windows.Controls.RadioButton]::new(); $rbFull.Content = 'OfflineScanAndFix'; $rbFull.FontSize = 12.5
        $modePanel.Children.Add($rbScan) | Out-Null
        $modePanel.Children.Add($rbSpot) | Out-Null
        $modePanel.Children.Add($rbFull) | Out-Null
        $sp.Children.Add($modePanel) | Out-Null
        $sp.Children.Add((New-ActionButton 'Run' {
            if ($ntfsVols.Count -eq 0) { return }
            $drv = $ntfsVols[$cmb.SelectedIndex].DriveLetter
            $mode = if ($rbFull.IsChecked) { 'OfflineScanAndFix' } elseif ($rbSpot.IsChecked) { 'SpotFix' } else { 'Scan' }
            Write-UILog "Running filesystem check ($mode) on $drv`:..."
            try {
                $params = @{ DriveLetter = $drv }
                switch ($mode) {
                    'Scan'              { $params.Scan = $true }
                    'SpotFix'           { $params.SpotFix = $true }
                    'OfflineScanAndFix' { $params.OfflineScanAndFix = $true }
                }
                $result = Repair-Volume @params
                Write-UILog "Result: $result"
            } catch { Write-UILog "ERROR: $($_.Exception.Message)" }
        })) | Out-Null
    })) | Out-Null

    # ── Optimize / TRIM ──
    $pnlTools.Children.Add((New-ToolSection 'Optimize and TRIM' 'Defragment HDDs or send TRIM commands to SSDs. Analyze shows fragmentation without making changes.' {
        param($sp)
        $cmb = [System.Windows.Controls.ComboBox]::new()
        $cmb.Width = 350; $cmb.Height = 28; $cmb.FontSize = 13
        $optVols = @(Get-Volume -ErrorAction SilentlyContinue | Where-Object { $_.DriveLetter -and $_.DriveType -eq 'Fixed' })
        foreach ($v in $optVols) { $cmb.Items.Add("$($v.DriveLetter): $($v.FileSystemLabel) ($($v.FileSystemType))") | Out-Null }
        if ($cmb.Items.Count -gt 0) { $cmb.SelectedIndex = 0 }
        New-ToolRow $sp 'Volume:' $cmb
        $modePanel = [System.Windows.Controls.StackPanel]::new()
        $modePanel.Orientation = 'Horizontal'
        $modePanel.Margin = [System.Windows.Thickness]::new(0, 0, 0, 6)
        $rbA = [System.Windows.Controls.RadioButton]::new(); $rbA.Content = 'Analyze'; $rbA.IsChecked = $true
        $rbA.Margin = [System.Windows.Thickness]::new(0,0,16,0); $rbA.FontSize = 12.5
        $rbD = [System.Windows.Controls.RadioButton]::new(); $rbD.Content = 'Defrag'
        $rbD.Margin = [System.Windows.Thickness]::new(0,0,16,0); $rbD.FontSize = 12.5
        $rbT = [System.Windows.Controls.RadioButton]::new(); $rbT.Content = 'TRIM (SSD)'; $rbT.FontSize = 12.5
        $modePanel.Children.Add($rbA) | Out-Null
        $modePanel.Children.Add($rbD) | Out-Null
        $modePanel.Children.Add($rbT) | Out-Null
        $sp.Children.Add($modePanel) | Out-Null
        $sp.Children.Add((New-ActionButton 'Run' {
            if ($optVols.Count -eq 0) { return }
            $drv = $optVols[$cmb.SelectedIndex].DriveLetter
            try {
                if ($rbT.IsChecked) {
                    Write-UILog "Sending TRIM to $drv`:..."
                    Optimize-Volume -DriveLetter $drv -ReTrim -ErrorAction Stop
                } elseif ($rbD.IsChecked) {
                    Write-UILog "Defragmenting $drv`:..."
                    Optimize-Volume -DriveLetter $drv -Defrag -ErrorAction Stop
                } else {
                    Write-UILog "Analyzing $drv`:..."
                    Optimize-Volume -DriveLetter $drv -Analyze -ErrorAction Stop
                }
                Write-UILog "Optimization complete."
            } catch { Write-UILog "ERROR: $($_.Exception.Message)" }
        })) | Out-Null
    })) | Out-Null

    # ── Secure Wipe ──
    $pnlTools.Children.Add((New-ToolSection 'Secure Wipe' 'Wipe free space (safe, preserves files) or entire disk (DESTROYS ALL DATA). Free space wipe uses cipher.exe.' {
        param($sp)
        $modePanel = [System.Windows.Controls.StackPanel]::new()
        $modePanel.Margin = [System.Windows.Thickness]::new(0, 0, 0, 6)
        $rbFree = [System.Windows.Controls.RadioButton]::new(); $rbFree.Content = 'Wipe free space only (safe)'
        $rbFree.IsChecked = $true; $rbFree.FontSize = 12.5
        $rbFree.Margin = [System.Windows.Thickness]::new(0,0,0,4)
        $rbDisk = [System.Windows.Controls.RadioButton]::new(); $rbDisk.Content = 'Wipe entire disk (DESTROYS ALL DATA)'
        $rbDisk.FontSize = 12.5; $rbDisk.Foreground = $script:brushConv.ConvertFrom('#C0392B')
        $modePanel.Children.Add($rbFree) | Out-Null
        $modePanel.Children.Add($rbDisk) | Out-Null
        $sp.Children.Add($modePanel) | Out-Null
        $cmbW = [System.Windows.Controls.ComboBox]::new()
        $cmbW.Width = 350; $cmbW.Height = 28; $cmbW.FontSize = 13
        $wipeVols = @(Get-Volume -ErrorAction SilentlyContinue | Where-Object { $_.DriveLetter -and $_.DriveType -eq 'Fixed' })
        foreach ($v in $wipeVols) { $cmbW.Items.Add("$($v.DriveLetter):") | Out-Null }
        if ($cmbW.Items.Count -gt 0) { $cmbW.SelectedIndex = 0 }
        New-ToolRow $sp 'Target:' $cmbW
        $sp.Children.Add((New-ActionButton 'Wipe' {
            if ($rbFree.IsChecked) {
                $drv = $wipeVols[$cmbW.SelectedIndex].DriveLetter
                if ([System.Windows.MessageBox]::Show("Wipe free space on $drv`:?`nThis is safe and does not delete files.", 'Confirm', 'YesNo', 'Question') -ne 'Yes') { return }
                Write-UILog "Wiping free space on $drv`:\ (this may take a while)..."
                $out = cipher /w:"$drv`:\" 2>&1
                Write-UILog ($out -join "`n")
                Write-UILog "Free space wipe complete."
            } else {
                $msg1 = "This will PERMANENTLY DESTROY ALL DATA on the selected disk.`n`nAre you sure?"
                if ([System.Windows.MessageBox]::Show($msg1, 'WARNING', 'YesNo', 'Warning') -ne 'Yes') { return }
                $msg2 = "FINAL CONFIRMATION: Type YES in the next dialog to proceed."
                if ([System.Windows.MessageBox]::Show($msg2, 'FINAL WARNING', 'YesNo', 'Error') -ne 'Yes') { return }
                $drv = $wipeVols[$cmbW.SelectedIndex].DriveLetter
                $part = Get-Partition -DriveLetter $drv -ErrorAction SilentlyContinue
                if ($part) {
                    Write-UILog "Wiping Disk $($part.DiskNumber)..."
                    Clear-Disk -Number $part.DiskNumber -RemoveData -RemoveOEM -Confirm:$false
                    Write-UILog "Disk wiped."
                    Load-Disks
                }
            }
        })) | Out-Null
    })) | Out-Null

    # ── Bootloader Repair ──
    $pnlTools.Children.Add((New-ToolSection 'Bootloader Repair' 'Rebuild the Windows bootloader using bcdboot.exe. Detects UEFI vs BIOS automatically.' {
        param($sp)
        $winParts = @(Get-Volume -ErrorAction SilentlyContinue |
            Where-Object { $_.DriveLetter -and (Test-Path "$($_.DriveLetter):\Windows\System32" -ErrorAction SilentlyContinue) })
        $cmb = [System.Windows.Controls.ComboBox]::new()
        $cmb.Width = 350; $cmb.Height = 28; $cmb.FontSize = 13
        foreach ($v in $winParts) { $cmb.Items.Add("$($v.DriveLetter): (Windows installation)") | Out-Null }
        if ($cmb.Items.Count -eq 0) { $cmb.Items.Add('(No Windows installations found)') | Out-Null; $cmb.IsEnabled = $false }
        $cmb.SelectedIndex = 0
        New-ToolRow $sp 'Windows:' $cmb
        $sp.Children.Add((New-ActionButton 'Repair' {
            if ($winParts.Count -eq 0) { return }
            $drv = $winParts[$cmb.SelectedIndex].DriveLetter
            if ([System.Windows.MessageBox]::Show("Repair bootloader for $drv`:\Windows?", 'Confirm', 'YesNo', 'Question') -ne 'Yes') { return }
            $isUefi = (Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control' -Name PEFirmwareType -ErrorAction SilentlyContinue).PEFirmwareType -eq 2
            $fw = if ($isUefi) { 'UEFI' } else { 'BIOS' }
            Write-UILog "Repairing bootloader ($fw mode)..."
            $out = bcdboot "$drv`:\Windows" /f $fw 2>&1
            Write-UILog ($out -join "`n")
        })) | Out-Null
    })) | Out-Null

    # ── Disk Benchmark ──
    $pnlTools.Children.Add((New-ToolSection 'Disk Benchmark' 'Test sequential and random 4K read/write performance. Creates a temporary 256MB test file.' {
        param($sp)
        $bmVols = @(Get-Volume -ErrorAction SilentlyContinue | Where-Object { $_.DriveLetter -and $_.DriveType -eq 'Fixed' -and $_.SizeRemaining -gt 300MB })
        $cmb = [System.Windows.Controls.ComboBox]::new()
        $cmb.Width = 350; $cmb.Height = 28; $cmb.FontSize = 13
        foreach ($v in $bmVols) { $cmb.Items.Add("$($v.DriveLetter): $(Format-DiskSize $v.SizeRemaining) free") | Out-Null }
        if ($cmb.Items.Count -gt 0) { $cmb.SelectedIndex = 0 }
        New-ToolRow $sp 'Drive:' $cmb
        $txtResult = [System.Windows.Controls.TextBox]::new()
        $txtResult.IsReadOnly = $true; $txtResult.FontFamily = [System.Windows.Media.FontFamily]::new('Consolas')
        $txtResult.FontSize = 12; $txtResult.Height = 110; $txtResult.TextWrapping = 'Wrap'
        $txtResult.VerticalScrollBarVisibility = 'Auto'
        $txtResult.Background = $script:brushConv.ConvertFrom('#F8F8F8')
        $txtResult.Margin = [System.Windows.Thickness]::new(0, 6, 0, 0)
        $sp.Children.Add((New-ActionButton 'Run Benchmark' {
            if ($bmVols.Count -eq 0) { return }
            $drv = $bmVols[$cmb.SelectedIndex].DriveLetter
            $testFile = "$drv`:\PartitionPilot_benchmark.tmp"
            $blockSize = 1MB
            $fileSize = 256MB
            $blocks = $fileSize / $blockSize
            $rndOps = 500
            $rndBlock = 4KB
            $sw = [System.Diagnostics.Stopwatch]::new()
            $buf = [byte[]]::new($blockSize)
            $rndBuf = [byte[]]::new($rndBlock)
            $results = @()
            try {
                Write-UILog "Benchmark started on $drv`:..."
                $txtResult.Text = "Running..."
                $window.Dispatcher.Invoke([System.Windows.Threading.DispatcherPriority]::Background, [action]{})
                # Sequential Write
                $sw.Restart()
                $fs = [System.IO.FileStream]::new($testFile, 'Create', 'Write', 'None', $blockSize, 'None')
                for ($i = 0; $i -lt $blocks; $i++) { $fs.Write($buf, 0, $blockSize) }
                $fs.Flush(); $fs.Close()
                $sw.Stop()
                $seqWMBs = [math]::Round(($fileSize / 1MB) / ($sw.ElapsedMilliseconds / 1000), 1)
                $results += "Sequential Write:  $seqWMBs MB/s"
                $window.Dispatcher.Invoke([System.Windows.Threading.DispatcherPriority]::Background, [action]{})
                # Sequential Read
                $sw.Restart()
                $fs = [System.IO.FileStream]::new($testFile, 'Open', 'Read', 'None', $blockSize, 'SequentialScan')
                while ($fs.Read($buf, 0, $blockSize) -gt 0) {}
                $fs.Close()
                $sw.Stop()
                $seqRMBs = [math]::Round(($fileSize / 1MB) / ($sw.ElapsedMilliseconds / 1000), 1)
                $results += "Sequential Read:   $seqRMBs MB/s"
                $window.Dispatcher.Invoke([System.Windows.Threading.DispatcherPriority]::Background, [action]{})
                # Random 4K Write
                $rng = [System.Random]::new(42)
                $maxPos = ($fileSize / $rndBlock) - 1
                $sw.Restart()
                $fs = [System.IO.FileStream]::new($testFile, 'Open', 'Write', 'None', $rndBlock, 'RandomAccess')
                for ($i = 0; $i -lt $rndOps; $i++) {
                    $pos = [long]($rng.Next(0, $maxPos)) * $rndBlock
                    $fs.Seek($pos, 'Begin') | Out-Null
                    $fs.Write($rndBuf, 0, $rndBlock)
                }
                $fs.Flush(); $fs.Close()
                $sw.Stop()
                $rnd4kW = [math]::Round($rndOps / ($sw.ElapsedMilliseconds / 1000), 0)
                $rnd4kWMB = [math]::Round(($rndOps * $rndBlock / 1MB) / ($sw.ElapsedMilliseconds / 1000), 1)
                $results += "Random 4K Write:   $rnd4kW IOPS  ($rnd4kWMB MB/s)"
                $window.Dispatcher.Invoke([System.Windows.Threading.DispatcherPriority]::Background, [action]{})
                # Random 4K Read
                $rng = [System.Random]::new(42)
                $sw.Restart()
                $fs = [System.IO.FileStream]::new($testFile, 'Open', 'Read', 'None', $rndBlock, 'RandomAccess')
                for ($i = 0; $i -lt $rndOps; $i++) {
                    $pos = [long]($rng.Next(0, $maxPos)) * $rndBlock
                    $fs.Seek($pos, 'Begin') | Out-Null
                    $fs.Read($rndBuf, 0, $rndBlock) | Out-Null
                }
                $fs.Close()
                $sw.Stop()
                $rnd4kR = [math]::Round($rndOps / ($sw.ElapsedMilliseconds / 1000), 0)
                $rnd4kRMB = [math]::Round(($rndOps * $rndBlock / 1MB) / ($sw.ElapsedMilliseconds / 1000), 1)
                $results += "Random 4K Read:    $rnd4kR IOPS  ($rnd4kRMB MB/s)"
                $txtResult.Text = $results -join "`r`n"
                Write-UILog "Benchmark complete."
            } catch {
                Write-UILog "ERROR: $($_.Exception.Message)"
                $txtResult.Text = "Error: $($_.Exception.Message)"
            } finally {
                Remove-Item $testFile -Force -ErrorAction SilentlyContinue
            }
        })) | Out-Null
        $sp.Children.Add($txtResult) | Out-Null
    })) | Out-Null
}

# ── Tab 4: Disk Images ─────────────────────────────────────────────────────────

function Build-ImagesPage {
    $pnlImages.Children.Clear()

    # ── Mount Image ──
    $pnlImages.Children.Add((New-ToolSection 'Mount Disk Image' 'Mount an ISO, VHD, or VHDX file. The image will appear as a new drive.' {
        param($sp)
        $pathRow = [System.Windows.Controls.StackPanel]::new()
        $pathRow.Orientation = 'Horizontal'
        $pathRow.Margin = [System.Windows.Thickness]::new(0, 0, 0, 6)
        $txtPath = [System.Windows.Controls.TextBox]::new()
        $txtPath.Width = 420; $txtPath.Height = 28; $txtPath.FontSize = 13
        $txtPath.Padding = [System.Windows.Thickness]::new(4,2,4,2)
        $pathRow.Children.Add($txtPath) | Out-Null
        $btnBrowse = [System.Windows.Controls.Button]::new()
        $btnBrowse.Content = 'Browse'; $btnBrowse.Width = 75; $btnBrowse.Height = 28
        $btnBrowse.Margin = [System.Windows.Thickness]::new(6,0,0,0); $btnBrowse.FontSize = 12
        $btnBrowse.Add_Click({
            $ofd = [Microsoft.Win32.OpenFileDialog]::new()
            $ofd.Filter = "Disk Images (*.iso;*.vhd;*.vhdx)|*.iso;*.vhd;*.vhdx|All Files|*.*"
            if ($ofd.ShowDialog($window)) { $txtPath.Text = $ofd.FileName }
        })
        $pathRow.Children.Add($btnBrowse) | Out-Null
        $sp.Children.Add($pathRow) | Out-Null
        $sp.Children.Add((New-ActionButton 'Mount' {
            $path = $txtPath.Text.Trim()
            if (-not $path -or -not (Test-Path $path)) {
                [System.Windows.MessageBox]::Show('Select a valid image file.', 'Mount', 'OK', 'Warning')
                return
            }
            try {
                Write-UILog "Mounting $path..."
                $img = Mount-DiskImage -ImagePath $path -PassThru
                $vol = $img | Get-Volume -ErrorAction SilentlyContinue
                $letter = if ($vol.DriveLetter) { "$($vol.DriveLetter):" } else { '(no letter assigned)' }
                Write-UILog "Mounted at $letter."
                [System.Windows.MessageBox]::Show("Image mounted at $letter.", 'Success', 'OK', 'Information')
                Refresh-MountedImages
            } catch {
                Write-UILog "ERROR: $($_.Exception.Message)"
                [System.Windows.MessageBox]::Show($_.Exception.Message, 'Error', 'OK', 'Error')
            }
        })) | Out-Null
    })) | Out-Null

    # ── Mounted images list ──
    $gb = [System.Windows.Controls.GroupBox]::new()
    $gb.Header = 'Currently Mounted Images'
    $gb.Margin = [System.Windows.Thickness]::new(0, 0, 0, 10)
    $gb.Padding = [System.Windows.Thickness]::new(10)
    $gb.Background = [System.Windows.Media.Brushes]::White
    $innerSp = [System.Windows.Controls.StackPanel]::new()
    $script:lstMounted = [System.Windows.Controls.ListView]::new()
    $script:lstMounted.Height = 140; $script:lstMounted.FontSize = 12.5
    $script:lstMounted.BorderThickness = [System.Windows.Thickness]::new(1)
    $gv = [System.Windows.Controls.GridView]::new()
    foreach ($h in @(@('Path',350), @('Type',60), @('Size',80), @('Drive',50))) {
        $col = [System.Windows.Controls.GridViewColumn]::new()
        $col.Header = $h[0]; $col.Width = $h[1]
        $col.DisplayMemberBinding = [System.Windows.Data.Binding]::new($h[0])
        $gv.Columns.Add($col)
    }
    $script:lstMounted.View = $gv
    $innerSp.Children.Add($script:lstMounted) | Out-Null
    $innerSp.Children.Add((New-ActionButton 'Dismount Selected' {
        $sel = $script:lstMounted.SelectedItem
        if (-not $sel) { [System.Windows.MessageBox]::Show('Select a mounted image.', 'Dismount', 'OK', 'Information'); return }
        try {
            Write-UILog "Dismounting $($sel.Path)..."
            Dismount-DiskImage -ImagePath $sel.Path
            Write-UILog "Dismounted."
            Refresh-MountedImages
        } catch {
            Write-UILog "ERROR: $($_.Exception.Message)"
            [System.Windows.MessageBox]::Show($_.Exception.Message, 'Error', 'OK', 'Error')
        }
    })) | Out-Null
    $gb.Content = $innerSp
    $pnlImages.Children.Add($gb) | Out-Null

    # ── Create VHD ──
    $pnlImages.Children.Add((New-ToolSection 'Create Virtual Hard Disk' 'Create a new VHD/VHDX file, initialize, format, and mount it. Uses diskpart (no Hyper-V required).' {
        param($sp)
        $pathRow = [System.Windows.Controls.StackPanel]::new()
        $pathRow.Orientation = 'Horizontal'
        $pathRow.Margin = [System.Windows.Thickness]::new(0, 0, 0, 6)
        $txtVhdPath = [System.Windows.Controls.TextBox]::new()
        $txtVhdPath.Width = 420; $txtVhdPath.Height = 28; $txtVhdPath.FontSize = 13
        $txtVhdPath.Padding = [System.Windows.Thickness]::new(4,2,4,2)
        $pathRow.Children.Add($txtVhdPath) | Out-Null
        $btnVhdBrowse = [System.Windows.Controls.Button]::new()
        $btnVhdBrowse.Content = 'Browse'; $btnVhdBrowse.Width = 75; $btnVhdBrowse.Height = 28
        $btnVhdBrowse.Margin = [System.Windows.Thickness]::new(6,0,0,0); $btnVhdBrowse.FontSize = 12
        $btnVhdBrowse.Add_Click({
            $sfd = [Microsoft.Win32.SaveFileDialog]::new()
            $sfd.Filter = "VHDX|*.vhdx|VHD|*.vhd"
            $sfd.DefaultExt = ".vhdx"
            if ($sfd.ShowDialog($window)) { $txtVhdPath.Text = $sfd.FileName }
        })
        $pathRow.Children.Add($btnVhdBrowse) | Out-Null
        New-ToolRow $sp 'File path:' $pathRow.Children[0]
        $sp.Children.RemoveAt($sp.Children.Count - 1)
        $sp.Children.Add($pathRow) | Out-Null

        $sizeRow = [System.Windows.Controls.StackPanel]::new()
        $sizeRow.Orientation = 'Horizontal'
        $txtSize = [System.Windows.Controls.TextBox]::new()
        $txtSize.Width = 100; $txtSize.Height = 28; $txtSize.Text = '20'; $txtSize.FontSize = 13
        $sizeRow.Children.Add($txtSize) | Out-Null
        $sizeLabel = [System.Windows.Controls.TextBlock]::new()
        $sizeLabel.Text = ' GB'; $sizeLabel.VerticalAlignment = 'Center'; $sizeLabel.FontSize = 13
        $sizeRow.Children.Add($sizeLabel) | Out-Null
        New-ToolRow $sp 'Size:' $sizeRow

        $typePanel = [System.Windows.Controls.StackPanel]::new()
        $typePanel.Orientation = 'Horizontal'
        $typePanel.Margin = [System.Windows.Thickness]::new(0, 0, 0, 6)
        $rbDyn = [System.Windows.Controls.RadioButton]::new(); $rbDyn.Content = 'Dynamic (grows as needed)'
        $rbDyn.IsChecked = $true; $rbDyn.Margin = [System.Windows.Thickness]::new(0,0,16,0); $rbDyn.FontSize = 12.5
        $rbFix = [System.Windows.Controls.RadioButton]::new(); $rbFix.Content = 'Fixed (full size immediately)'
        $rbFix.FontSize = 12.5
        $typePanel.Children.Add($rbDyn) | Out-Null
        $typePanel.Children.Add($rbFix) | Out-Null
        $sp.Children.Add($typePanel) | Out-Null

        $sp.Children.Add((New-ActionButton 'Create VHD' {
            $path = $txtVhdPath.Text.Trim()
            if (-not $path) { [System.Windows.MessageBox]::Show('Specify a file path.', 'Create VHD', 'OK', 'Warning'); return }
            $sizeMB = [int]([double]$txtSize.Text * 1024)
            $vhdType = if ($rbFix.IsChecked) { 'fixed' } else { 'expandable' }
            try {
                Write-UILog "Creating VHD: $path ($sizeMB MB, $vhdType)..."
                $dp = "create vdisk file=`"$path`" maximum=$sizeMB type=$vhdType`nselect vdisk file=`"$path`"`nattach vdisk`ncreate partition primary`nformat fs=ntfs quick label=`"VHD`"`nassign"
                $out = $dp | diskpart 2>&1
                $dpText = $out | Out-String
                if ($dpText -match 'error') { throw "diskpart: $dpText" }
                Write-UILog "VHD created and mounted."
                [System.Windows.MessageBox]::Show("VHD created at $path.", 'Success', 'OK', 'Information')
                Refresh-MountedImages
                Load-Disks
            } catch {
                Write-UILog "ERROR: $($_.Exception.Message)"
                [System.Windows.MessageBox]::Show($_.Exception.Message, 'Error', 'OK', 'Error')
            }
        })) | Out-Null
    })) | Out-Null

    Refresh-MountedImages
}

function Refresh-MountedImages {
    if (-not $script:lstMounted) { return }
    $script:lstMounted.Items.Clear()
    $images = @(Get-DiskImage -ErrorAction SilentlyContinue |
        Where-Object { $_.Attached -and $_.ImagePath })
    foreach ($img in $images) {
        $vol = $img | Get-Volume -ErrorAction SilentlyContinue
        $letter = if ($vol -and $vol.DriveLetter) { "$($vol.DriveLetter):" } else { '' }
        $type = switch ($img.StorageType) { 1 {'ISO'}; 2 {'VHD'}; 3 {'VHDX'}; default {'?'} }
        $script:lstMounted.Items.Add([PSCustomObject]@{
            Path  = $img.ImagePath
            Type  = $type
            Size  = Format-DiskSize $img.Size
            Drive = $letter
        }) | Out-Null
    }
}

# ── Event handlers ──────────────────────────────────────────────────────────────

$cmbDisk.Add_SelectionChanged({
    $idx = $cmbDisk.SelectedIndex
    if ($idx -ge 0 -and $idx -lt $script:allDisks.Count) {
        $dn = $script:allDisks[$idx].Number
        Write-UILog "Loading Disk $dn..."
        Load-Partitions $dn
        Write-UILog "Disk $dn loaded."
    }
})

$btnRefresh.Add_Click({
    Write-UILog "Refreshing..."
    Load-Disks
})

$btnCreate.Add_Click({ Invoke-CreatePartition })
$btnDelete.Add_Click({ Invoke-DeletePartition })
$btnFormat.Add_Click({ Invoke-FormatPartition })
$btnResize.Add_Click({ Invoke-ResizePartition })
$btnExtend.Add_Click({ Invoke-ExtendPartition })
$btnSplit.Add_Click({ Invoke-SplitPartition })
$btnLetter.Add_Click({ Invoke-ChangeLetter })
$btnActive.Add_Click({ Invoke-SetActive })
$btnHide.Add_Click({ Invoke-ToggleHide })

$cmbHealthDisk.Add_SelectionChanged({ Load-HealthData })
$btnRefreshHealth.Add_Click({ Load-PhysicalDisks; Load-HealthData })

$tabMain.Add_SelectionChanged({
    switch ($tabMain.SelectedIndex) {
        1 { if ($cmbHealthDisk.Items.Count -eq 0) { Load-PhysicalDisks } }
        2 { Build-ToolsPage }
        3 { Build-ImagesPage }
    }
})

# ── Initialize ──────────────────────────────────────────────────────────────────

Write-UILog "PartitionPilot ready. Select a disk to begin."
Load-Disks
$window.ShowDialog() | Out-Null
