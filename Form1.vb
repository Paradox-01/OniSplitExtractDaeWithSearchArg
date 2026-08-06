Imports System.Diagnostics
Imports System.IO
Imports System.Linq
Imports System.Security
Imports System.Security.Cryptography
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Threading.Tasks
Imports System.Xml
Imports System.Xml.Linq

Public Class Form1
    Private Const DefaultTestEnvironmentFileName As String = "DefaultTestEnvironment.txt"
    Private Const SettingsFileName As String = "Settings.xml"
    Private Const ReportFileName As String = "folder-comparison-report.html"
    Private Const WikiReportFileName As String = "folder-comparison-report-wiki.txt"

    Private ReadOnly testEnvironmentTextBox As New TextBox()
    Private ReadOnly browseButton As New Button()
    Private ReadOnly compareButton As New Button()
    Private ReadOnly openReportButton As New Button()
    Private ReadOnly wikifyButton As New Button()
    Private ReadOnly statusLabel As New Label()
    Private ReadOnly reportPathLabel As New Label()
    Private comparisonInProgress As Boolean
    Private reportPath As String = String.Empty
    Private latestComparisons As List(Of FolderComparison)
    Private latestEnvironmentPath As String = String.Empty

    Private Class FileComparison
        Public Property RelativePath As String
        Public Property Status As String
        Public Property NoSearchSize As String
        Public Property SearchSize As String
        Public Property NoSearchHash As String
        Public Property SearchHash As String
    End Class

    Private Class FolderComparison
        Public Property PairName As String
        Public Property NoSearchFolder As String
        Public Property SearchFolder As String
        Public Property Files As New List(Of FileComparison)()
        Public Property MissingFolder As String
    End Class

    Public Sub New()
        InitializeComponent()
        InitializeUserInterface()
    End Sub

    Private Sub InitializeUserInterface()
        Text = "Oni Geometry Folder Comparison"
        MinimumSize = New Size(720, 260)

        Dim menuStrip As New MenuStrip()
        Dim settingsMenu As New ToolStripMenuItem("Settings")
        AddHandler settingsMenu.Click, AddressOf SettingsMenu_Click
        menuStrip.Items.Add(settingsMenu)
        MainMenuStrip = menuStrip
        Controls.Add(menuStrip)

        Dim environmentLabel As New Label With {
            .AutoSize = True,
            .Left = 16,
            .Top = 55,
            .Text = "GameDataFolder:"
        }
        Controls.Add(environmentLabel)

        testEnvironmentTextBox.Left = 16
        testEnvironmentTextBox.Top = 78
        testEnvironmentTextBox.Width = 560
        testEnvironmentTextBox.Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right
        Controls.Add(testEnvironmentTextBox)

        browseButton.Text = "Browse..."
        browseButton.Left = 584
        browseButton.Top = 76
        browseButton.Width = 100
        browseButton.Anchor = AnchorStyles.Top Or AnchorStyles.Right
        AddHandler browseButton.Click, AddressOf BrowseButton_Click
        Controls.Add(browseButton)

        compareButton.Text = "Compare folders"
        compareButton.Left = 16
        compareButton.Top = 120
        compareButton.Width = 145
        AddHandler compareButton.Click, AddressOf CompareButton_Click
        Controls.Add(compareButton)

        openReportButton.Text = "Open HTML report"
        openReportButton.Left = 175
        openReportButton.Top = 120
        openReportButton.Width = 145
        openReportButton.Enabled = False
        AddHandler openReportButton.Click, AddressOf OpenReportButton_Click
        Controls.Add(openReportButton)

        wikifyButton.Text = "Wikify"
        wikifyButton.Left = 334
        wikifyButton.Top = 120
        wikifyButton.Width = 100
        wikifyButton.Enabled = False
        AddHandler wikifyButton.Click, AddressOf WikifyButton_Click
        Controls.Add(wikifyButton)

        statusLabel.AutoSize = True
        statusLabel.Left = 16
        statusLabel.Top = 170
        statusLabel.Text = "Ready."
        Controls.Add(statusLabel)

        reportPathLabel.AutoSize = True
        reportPathLabel.Left = 16
        reportPathLabel.Top = 198
        reportPathLabel.ForeColor = Color.DimGray
        Controls.Add(reportPathLabel)
    End Sub

    Private Sub Form1_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        LoadSettings()
        RestoreWindowBounds()
    End Sub

    Private Sub Form1_FormClosing(sender As Object, e As FormClosingEventArgs) Handles MyBase.FormClosing
        SaveSettings()
    End Sub

    Private Sub BrowseButton_Click(sender As Object, e As EventArgs)
        Using dialog As New FolderBrowserDialog With {.Description = "Select the GameDataFolder containing the level folder pairs."}
            dialog.SelectedPath = testEnvironmentTextBox.Text
            If dialog.ShowDialog(Me) = DialogResult.OK Then
                testEnvironmentTextBox.Text = dialog.SelectedPath
            End If
        End Using
    End Sub

    Private Async Sub CompareButton_Click(sender As Object, e As EventArgs)
        If comparisonInProgress Then Return

        Dim environmentPath = testEnvironmentTextBox.Text.Trim()
        If Not Directory.Exists(environmentPath) Then
            MessageBox.Show(Me, "The selected GameDataFolder does not exist.", "Folder not found", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If

        comparisonInProgress = True
        compareButton.Enabled = False
        openReportButton.Enabled = False
        wikifyButton.Enabled = False
        statusLabel.Text = "Comparing folders..."

        Try
            Dim comparisons = Await Task.Run(Function() CompareFolders(environmentPath))
            latestComparisons = comparisons
            latestEnvironmentPath = environmentPath
            reportPath = Path.Combine(environmentPath, ReportFileName)
            WriteHtmlReport(reportPath, environmentPath, comparisons)
            openReportButton.Enabled = True
            wikifyButton.Enabled = True
            reportPathLabel.Text = "Report: " & reportPath
            statusLabel.Text = String.Format("Finished: {0} folder pairs, {1} file results.", comparisons.Count, comparisons.Sum(Function(item) item.Files.Count))
            SaveSettings()
        Catch ex As DirectoryNotFoundException
            ShowComparisonError(ex.Message)
        Catch ex As IOException
            ShowComparisonError(ex.Message)
        Catch ex As UnauthorizedAccessException
            ShowComparisonError(ex.Message)
        Catch ex As SecurityException
            ShowComparisonError(ex.Message)
        Finally
            comparisonInProgress = False
            compareButton.Enabled = True
        End Try
    End Sub

    Private Sub ShowComparisonError(message As String)
        statusLabel.Text = "Comparison failed."
        MessageBox.Show(Me, message, "Comparison failed", MessageBoxButtons.OK, MessageBoxIcon.Error)
    End Sub

    Private Sub OpenReportButton_Click(sender As Object, e As EventArgs)
        If String.IsNullOrWhiteSpace(reportPath) OrElse Not File.Exists(reportPath) Then Return
        Process.Start(New ProcessStartInfo With {.FileName = reportPath, .UseShellExecute = True})
    End Sub

    Private Sub WikifyButton_Click(sender As Object, e As EventArgs)
        If latestComparisons Is Nothing OrElse String.IsNullOrWhiteSpace(latestEnvironmentPath) Then Return

        Dim wikiPath = Path.Combine(latestEnvironmentPath, WikiReportFileName)
        Try
            WriteMediaWikiReport(wikiPath, latestEnvironmentPath, latestComparisons)
            reportPathLabel.Text = "Wiki report: " & wikiPath
            statusLabel.Text = "MediaWiki report generated with differences only."
            Process.Start(New ProcessStartInfo With {.FileName = wikiPath, .UseShellExecute = True})
        Catch ex As IOException
            ShowComparisonError(ex.Message)
        Catch ex As UnauthorizedAccessException
            ShowComparisonError(ex.Message)
        Catch ex As SecurityException
            ShowComparisonError(ex.Message)
        End Try
    End Sub

    Private Function CompareFolders(environmentPath As String) As List(Of FolderComparison)
        Dim folders = Directory.GetDirectories(environmentPath, "level*_geometry_*", SearchOption.TopDirectoryOnly)
        Dim pairs As New Dictionary(Of String, FolderComparison)(StringComparer.OrdinalIgnoreCase)
        Dim pattern As New Regex("^level(.+)_geometry_(no_search|search)$", RegexOptions.IgnoreCase)

        For Each folder In folders
            Dim match = pattern.Match(Path.GetFileName(folder))
            If Not match.Success Then Continue For

            Dim pairName = "level" & match.Groups(1).Value
            If Not pairs.ContainsKey(pairName) Then
                pairs.Add(pairName, New FolderComparison With {.PairName = pairName})
            End If

            If match.Groups(2).Value.Equals("no_search", StringComparison.OrdinalIgnoreCase) Then
                pairs(pairName).NoSearchFolder = folder
            Else
                pairs(pairName).SearchFolder = folder
            End If
        Next

        For Each pair In pairs.Values
            If String.IsNullOrEmpty(pair.NoSearchFolder) Then
                pair.MissingFolder = "no_search folder"
            ElseIf String.IsNullOrEmpty(pair.SearchFolder) Then
                pair.MissingFolder = "search folder"
            Else
                pair.Files.AddRange(CompareFiles(pair.NoSearchFolder, pair.SearchFolder))
            End If
        Next

        Return pairs.Values.OrderBy(Function(pair) GetLevelSortKey(pair.PairName)).ThenBy(Function(pair) pair.PairName, StringComparer.OrdinalIgnoreCase).ToList()
    End Function

    Private Shared Function GetLevelSortKey(pairName As String) As Integer
        Dim levelNumber As Integer
        If pairName.StartsWith("level", StringComparison.OrdinalIgnoreCase) AndAlso Integer.TryParse(pairName.Substring(5), levelNumber) Then
            Return levelNumber
        End If
        Return Integer.MaxValue
    End Function

    Private Function CompareFiles(noSearchFolder As String, searchFolder As String) As List(Of FileComparison)
        Dim noSearchFiles = Directory.GetFiles(noSearchFolder, "*", SearchOption.AllDirectories).ToDictionary(
            Function(file) Path.GetRelativePath(noSearchFolder, file), StringComparer.OrdinalIgnoreCase)
        Dim searchFiles = Directory.GetFiles(searchFolder, "*", SearchOption.AllDirectories).ToDictionary(
            Function(file) Path.GetRelativePath(searchFolder, file), StringComparer.OrdinalIgnoreCase)
        Dim relativePaths = noSearchFiles.Keys.Union(searchFiles.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(Function(path) path, StringComparer.OrdinalIgnoreCase)
        Dim results As New List(Of FileComparison)()

        For Each relativePath In relativePaths
            Dim noSearchFile As String = Nothing
            Dim searchFile As String = Nothing
            noSearchFiles.TryGetValue(relativePath, noSearchFile)
            searchFiles.TryGetValue(relativePath, searchFile)

            Dim result As New FileComparison With {.RelativePath = relativePath}
            If noSearchFile Is Nothing Then
                result.Status = "Added in search"
                result.SearchSize = FormatFileSize(New FileInfo(searchFile).Length)
                result.SearchHash = ComputeHash(searchFile)
            ElseIf searchFile Is Nothing Then
                result.Status = "Removed in search"
                result.NoSearchSize = FormatFileSize(New FileInfo(noSearchFile).Length)
                result.NoSearchHash = ComputeHash(noSearchFile)
            Else
                result.NoSearchSize = FormatFileSize(New FileInfo(noSearchFile).Length)
                result.SearchSize = FormatFileSize(New FileInfo(searchFile).Length)
                result.NoSearchHash = ComputeHash(noSearchFile)
                result.SearchHash = ComputeHash(searchFile)
                result.Status = If(result.NoSearchHash.Equals(result.SearchHash, StringComparison.OrdinalIgnoreCase), "Unchanged", "Changed")
            End If
            results.Add(result)
        Next

        Return results
    End Function

    Private Shared Function ComputeHash(filePath As String) As String
        Using algorithm = SHA256.Create()
            Using stream = File.OpenRead(filePath)
                Return Convert.ToHexString(algorithm.ComputeHash(stream)).ToLowerInvariant()
            End Using
        End Using
    End Function

    Private Shared Function FormatFileSize(length As Long) As String
        Return length.ToString("N0") & " bytes"
    End Function

    Private Shared Sub WriteHtmlReport(path As String, environmentPath As String, comparisons As List(Of FolderComparison))
        Dim builder As New StringBuilder()
        builder.AppendLine("<!doctype html><html lang='en'><head><meta charset='utf-8'>")
        builder.AppendLine("<title>Geometry folder comparison</title><style>body{font:14px Segoe UI,Arial;margin:24px;color:#222}table{border-collapse:collapse;width:100%;margin:8px 0 28px}th,td{border:1px solid #ccc;padding:6px;text-align:left;vertical-align:top}th{background:#eee}.Changed{background:#fff2cc}.Added{background:#fff2cc}.Removed{background:#fff2cc}.Unchanged{color:#666}.missing{color:#a00;font-weight:bold}.legend{background:#f7f7f7;border:1px solid #ccc;padding:10px 14px;margin:14px 0}.legend p{margin:4px 0}.batch-panel{margin:14px 0}.batch-panel pre{background:#f7f7f7;border:1px solid #ccc;padding:12px;overflow:auto}.diff-only .unchanged-row{display:none}#toggle-diff{padding:6px 12px;margin:8px 0 16px}code{word-break:break-all}</style></head><body class='diff-only'>")
        Dim oniSplitVersion = GetOniSplitVersion(environmentPath)
        builder.AppendLine("<h1>Geometry folder comparison</h1>")
        builder.AppendLine("<p>What is the difference between not using and using the -search argument?</p>")
        builder.AppendLine("<p>OniSplit " & HtmlEncode(oniSplitVersion) & " was used.</p>")
        builder.AppendLine("<p>Generated: " & HtmlEncode(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")) & "</p>")
        builder.AppendLine("<div class='legend'><strong>Legend</strong><p><strong>Changed:</strong> Files exists in both folders.</p><p><strong>Added in search:</strong> This file exists additionally in the folder for which the -search argument was used.</p></div>")
        Dim batchCode = String.Join(Environment.NewLine, New String() {
            "@echo off",
            "",
            "OniSplit.exe -export ..\GameDataFolder\level0_Final ..\GameDataFolder\level0_Final.dat",
            "",
            "for %%X in (1 2 3 4 6 8 9 10 11 12 13 14 18 19) do (",
            "    OniSplit.exe -export ..\GameDataFolder\level%%X_Final ..\GameDataFolder\level%%X_Final.dat",
            "    OniSplit.exe -extract:dae ..\GameDataFolder\level%%X_geometry_search ..\GameDataFolder\level%%X_Final\AKEV*.oni -search ..\GameDataFolder\level0_Final",
            "    OniSplit.exe -extract:dae ..\GameDataFolder\level%%X_geometry_no_search ..\GameDataFolder\level%%X_Final\AKEV*.oni",
            ")",
            "",
            "pause"})
        builder.AppendLine("<details class='batch-panel' open><summary>Batch code used to output the files</summary><p>Note: The test batch file was run from inside the Tools directory where OniSplit.exe is located.</p><pre>" & HtmlEncode(batchCode) & "</pre></details>")
        builder.AppendLine("<button id='toggle-diff' type='button' onclick='toggleDiffOnly()'>Show all files</button>")
        builder.AppendLine("<script>function toggleDiffOnly(){var body=document.body;var button=document.getElementById('toggle-diff');var showingAll=!body.classList.toggle('diff-only');button.textContent=showingAll?'Show diff only':'Show all files';}</script>")

        For Each pair In comparisons
            builder.AppendLine("<h2>" & HtmlEncode(pair.PairName) & "</h2>")
            If Not String.IsNullOrEmpty(pair.MissingFolder) Then
                builder.AppendLine("<p class='missing'>Missing " & HtmlEncode(pair.MissingFolder) & ".</p>")
                Continue For
            End If

            builder.AppendLine("<table><thead><tr><th style='width:8%'>Status</th><th style='width:12%'>Relative file</th><th style='width:10%'>No search size</th><th style='width:10%'>Search size</th><th style='width:30%'>No search SHA-256</th><th style='width:30%'>Search SHA-256</th></tr></thead><tbody>")
            For Each file In pair.Files
                Dim cssClass = file.Status.Split(" "c)(0)
                Dim rowClass = If(file.Status.Equals("Unchanged", StringComparison.Ordinal), "unchanged-row ", "diff-row ") & cssClass
                builder.AppendLine("<tr class='" & rowClass & "'><td>" & HtmlEncode(file.Status) & "</td><td><code>" & HtmlEncode(file.RelativePath) & "</code></td><td>" & HtmlEncode(file.NoSearchSize) & "</td><td>" & HtmlEncode(file.SearchSize) & "</td><td><code>" & HtmlEncode(file.NoSearchHash) & "</code></td><td><code>" & HtmlEncode(file.SearchHash) & "</code></td></tr>")
            Next
            builder.AppendLine("</tbody></table>")
        Next

        File.WriteAllText(path, builder.ToString(), New UTF8Encoding(False))
    End Sub

    Private Shared Sub WriteMediaWikiReport(path As String, environmentPath As String, comparisons As List(Of FolderComparison))
        Dim builder As New StringBuilder()
        Dim oniSplitVersion = GetOniSplitVersion(environmentPath)
        builder.AppendLine("== Geometry folder comparison ==")
        builder.AppendLine()
        builder.AppendLine("What is the difference between not using and using the -search argument?")
        builder.AppendLine()
        builder.AppendLine("OniSplit " & WikiEncode(oniSplitVersion) & " was used.")
        builder.AppendLine()
        builder.AppendLine("Generated: " & DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
        'builder.AppendLine("Source: <code>" & WikiEncode(environmentPath) & "</code>")
        builder.AppendLine()
        builder.AppendLine("'''Legend:'''")
        builder.AppendLine("* '''Changed:''' Files exist in both folders.")
        builder.AppendLine("* '''Added in search:''' The file exists additionally in the folder using the <code>-search</code> argument.")
        builder.AppendLine()
        builder.AppendLine("=== Batch source ===")
        builder.AppendLine("<pre>")
        builder.AppendLine("@echo off")
        builder.AppendLine("OniSplit.exe -export ..\GameDataFolder\level0_Final ..\GameDataFolder\level0_Final.dat")
        builder.AppendLine("for %%X in (1 2 3 4 6 8 9 10 11 12 13 14 18 19) do (")
        builder.AppendLine("    OniSplit.exe -export ..\GameDataFolder\level%%X_Final ..\GameDataFolder\level%%X_Final.dat")
        builder.AppendLine("    OniSplit.exe -extract:dae ..\GameDataFolder\level%%X_geometry_search ..\GameDataFolder\level%%X_Final\AKEV*.oni -search ..\GameDataFolder\level0_Final")
        builder.AppendLine("    OniSplit.exe -extract:dae ..\GameDataFolder\level%%X_geometry_no_search ..\GameDataFolder\level%%X_Final\AKEV*.oni")
        builder.AppendLine(")")
        builder.AppendLine("</pre>")
        builder.AppendLine()

        For Each pair In comparisons
            builder.AppendLine("==== " & WikiEncode(pair.PairName) & " ====")
            If Not String.IsNullOrEmpty(pair.MissingFolder) Then
                builder.AppendLine("'''Missing " & WikiEncode(pair.MissingFolder) & ".'''")
                builder.AppendLine()
                Continue For
            End If

            Dim differences = pair.Files.Where(Function(file) Not file.Status.Equals("Unchanged", StringComparison.Ordinal)).ToList()
            If differences.Count = 0 Then Continue For
            builder.AppendLine("{| class=""wikitable sortable"" style=""width:100%;""")
            builder.AppendLine("! style=""width:8%;"" | Status")
            builder.AppendLine("! style=""width:12%;"" | Relative file")
            builder.AppendLine("! style=""width:10%;"" | No search size")
            builder.AppendLine("! style=""width:10%;"" | Search size")
            builder.AppendLine("! style=""width:30%;"" | No search SHA-256")
            builder.AppendLine("! style=""width:30%;"" | Search SHA-256")
            For Each file In differences
                builder.AppendLine("|-")
                builder.AppendLine("| " & WikiEncode(file.Status) & " || " & WikiEncode(file.RelativePath) & " || " & WikiEncode(file.NoSearchSize) & " || " & WikiEncode(file.SearchSize) & " || <code>" & WikiEncode(file.NoSearchHash) & "</code> || <code>" & WikiEncode(file.SearchHash) & "</code>")
            Next
            builder.AppendLine("|}")
            builder.AppendLine()
        Next

        builder.AppendLine("[[Category:Tool supporting pages]]")
        File.WriteAllText(path, builder.ToString(), New UTF8Encoding(False))
    End Sub

    Private Shared Function GetOniSplitVersion(environmentPath As String) As String
        Dim candidates As New List(Of String) From {
            Path.Combine(AppContext.BaseDirectory, "OniSplit.exe"),
            Path.Combine(AppContext.BaseDirectory, "Tools", "OniSplit.exe")
        }
        Dim environmentParent = Directory.GetParent(environmentPath)
        If environmentParent IsNot Nothing Then candidates.Add(Path.Combine(environmentParent.FullName, "Tools", "OniSplit.exe"))

        For Each candidate In candidates.Distinct(StringComparer.OrdinalIgnoreCase)
            If File.Exists(candidate) Then
                Dim version = FileVersionInfo.GetVersionInfo(candidate).FileVersion
                If Not String.IsNullOrWhiteSpace(version) Then Return FormatOniSplitVersion(version)
            End If
        Next

        Return "version unavailable"
    End Function

    Private Shared Function FormatOniSplitVersion(versionText As String) As String
        Dim parsedVersion As Version = Nothing
        If Not Version.TryParse(versionText.Trim(), parsedVersion) Then Return "version unavailable"

        Return String.Format("{0}.{1}.{2}.{3}",
                             parsedVersion.Major,
                             parsedVersion.Minor,
                             Math.Max(parsedVersion.Build, 0),
                             Math.Max(parsedVersion.Revision, 0))
    End Function

    Private Shared Function WikiEncode(value As String) As String
        Return If(value, String.Empty).Replace("|", "{{!}}").Replace(vbCr, "").Replace(vbLf, "<br>")
    End Function

    Private Shared Function HtmlEncode(value As String) As String
        Return System.Net.WebUtility.HtmlEncode(If(value, String.Empty))
    End Function

    Private ReadOnly Property SettingsPath As String
        Get
            Return Path.Combine(AppContext.BaseDirectory, SettingsFileName)
        End Get
    End Property

    Private Sub LoadSettings()
        Dim environmentPath = ReadDefaultEnvironmentPath()
        If String.IsNullOrWhiteSpace(environmentPath) Then environmentPath = PromptForEnvironmentPath()
        testEnvironmentTextBox.Text = environmentPath
        If String.IsNullOrWhiteSpace(environmentPath) Then statusLabel.Text = "Select a GameDataFolder before comparing."
    End Sub

    Private Shared Function ReadDefaultEnvironmentPath() As String
        Dim environmentFilePath = System.IO.Path.Combine(AppContext.BaseDirectory, DefaultTestEnvironmentFileName)
        If Not File.Exists(environmentFilePath) Then Return String.Empty

        Try
            Return File.ReadAllText(environmentFilePath).Trim()
        Catch ex As IOException
            Return String.Empty
        Catch ex As UnauthorizedAccessException
            Return String.Empty
        End Try
    End Function

    Private Function PromptForEnvironmentPath() As String
        Using dialog As New FolderBrowserDialog With {.Description = "Select the GameDataFolder containing the level folder pairs."}
            If dialog.ShowDialog(Me) = DialogResult.OK Then Return dialog.SelectedPath
        End Using
        Return String.Empty
    End Function

    Private Sub SaveSettings()
        Dim document As New XDocument(New XElement("Settings",
            New XElement("TestEnvironment", testEnvironmentTextBox.Text),
            New XElement("Window", New XAttribute("left", Left), New XAttribute("top", Top), New XAttribute("width", Width), New XAttribute("height", Height), New XAttribute("state", WindowState.ToString()))))
        document.Save(SettingsPath)
    End Sub

    Private Sub RestoreWindowBounds()
        If Not File.Exists(SettingsPath) Then Return
        Try
            Dim windowElement = XDocument.Load(SettingsPath).Root.Element("Window")
            If windowElement Is Nothing Then Return
            Dim bounds = New Rectangle(Integer.Parse(windowElement.Attribute("left").Value), Integer.Parse(windowElement.Attribute("top").Value), Integer.Parse(windowElement.Attribute("width").Value), Integer.Parse(windowElement.Attribute("height").Value))
            If Screen.AllScreens.Any(Function(screen) screen.WorkingArea.IntersectsWith(bounds)) Then bounds = bounds
            Dim savedState As FormWindowState
            If [Enum].TryParse(windowElement.Attribute("state").Value, savedState) Then WindowState = savedState
        Catch ex As XmlException
            statusLabel.Text = "Window settings could not be restored."
        Catch ex As FormatException
            statusLabel.Text = "Window settings could not be restored."
        Catch ex As NullReferenceException
            statusLabel.Text = "Window settings could not be restored."
        End Try
    End Sub

    Private Sub SettingsMenu_Click(sender As Object, e As EventArgs)
        SaveSettings()
        Dim editor = If(File.Exists("C:\Program Files\Notepad++\notepad++.exe"), "C:\Program Files\Notepad++\notepad++.exe", "notepad.exe")
        Process.Start(New ProcessStartInfo With {.FileName = editor, .Arguments = ChrW(34) & SettingsPath & ChrW(34), .UseShellExecute = True})
    End Sub
End Class
