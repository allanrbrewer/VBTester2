Imports System.Collections.Generic
Imports System.IO
Imports System.Threading
Imports System.Diagnostics ' For process management
Imports CommandLineParser ' NuGet package for command-line parsing
Imports NLog ' NuGet package for logging

Public Class Main
    Private Shared ReadOnly LOG As NLog.Logger = NLog.LogManager.GetCurrentClassLogger()
    Private Shared playerStats As PlayerStats
    Private Shared t As Integer
    Private Shared finished As Integer = 0

    Public Shared Sub Main(args As String())
        Try
            Dim options As New Options()
            With options
                .AddOption("h", "help", False, "Print the help")
                .AddOption("v", "verbose", False, "Verbose mode. Spam incoming.")
                .AddOption("n", "games", True, "Number of games to play. Default 1.")
                .AddOption("t", "threads", True, "Number of thread to spawn for the games. Default 1.")
                .AddOption("r", "referee", True, "Required. Referee command line.")
                .AddOption("p1", "player1", True, "Required. Player 1 command line.")
                .AddOption("p2", "player2", True, "Required. Player 2 command line.")
                .AddOption("p3", "player3", True, "Player 3 command line.")
                .AddOption("p4", "player4", True, "Player 4 command line.")
                .AddOption("l", "logs", True, "A directory for games logs")
                .AddOption("s", "swap", False, "Swap player positions")
                .AddOption("i", "seed", True, "Initial seed. For repeatable tests")
                .AddOption("o", "old", False, "Old mode")
            End With

            Dim parser As New CommandLine.Parser(Sub(settings) settings.HelpWriter = Console.Error)
            Dim result = parser.ParseArguments(Of CommandLineOptions)(args)

            If result.Errors.Any Then
                Console.Error.WriteLine(result.Errors.ToString())
                System.Environment.Exit(1)
            End If


            Dim cmd As CommandLineOptions = result.Value

            ' Need help?
            If cmd.Help OrElse String.IsNullOrEmpty(cmd.Referee) OrElse String.IsNullOrEmpty(cmd.Player1) OrElse String.IsNullOrEmpty(cmd.Player2) Then
                parser.ShowHelp(Console.Error)
                System.Environment.Exit(0)
            End If

            ' Verbose mode (configure NLog for verbose logging if needed)
            If cmd.Verbose Then
                ' Configure NLog for verbose logging here (e.g., set minimum level to Debug/Trace)
                LOG.Info("Verbose mode activated")
            End If

            ' Referee command line
            Dim refereeCmd As String = cmd.Referee
            LOG.Info("Referee command line: " & refereeCmd)

            ' Players command lines
            Dim playersCmd As New List(Of String)
            For i As Integer = 1 To 4
                Dim playerOption As String = "p" & i
                If cmd.GetType().GetProperty(playerOption) IsNot Nothing Then
                    Dim value As String = cmd.GetType().GetProperty(playerOption).GetValue(cmd, Nothing)?.ToString()
                    If value IsNot Nothing Then
                        playersCmd.Add(value)
                        LOG.Info("Player " & i & " command line: " & value)
                    End If
                End If
            Next

            ' Games count
            Dim n As Integer = 1
            Try
                n = Integer.Parse(cmd.Games)
            Catch exception As Exception
            End Try
            LOG.Info("Number of games to play: " & n)


            ' Thread count
            t = 1
            Try
                t = Integer.Parse(cmd.Threads)
            Catch exception As Exception
            End Try

            LOG.Info("Number of threads to spawn: " & t)

            ' Logs directory
            Dim logs As String = Nothing
            If cmd.Logs IsNot Nothing Then
                logs = cmd.Logs
                If Not Directory.Exists(logs) Then
                    Throw New DirectoryNotFoundException("Given path for the logs directory is not a directory: " & logs)
                End If
            End If

            Dim swap As Boolean = cmd.Swap

            ' Seed Initialization
            If cmd.Seed IsNot Nothing Then
                Dim newSeed As Long = Long.Parse(cmd.Seed)
                SeedGenerator.initialSeed(newSeed) ' Assuming you have a SeedGenerator class
                LOG.Info("Initial Seed: " & newSeed)
            End If

            ' Prepare stats objects
            playerStats = New PlayerStats(playersCmd.Count) ' Assuming you have a PlayerStats class
            Dim count As New Mutable(Of Integer)(0) ' Assuming you have a Mutable class

            ' Start the threads
            For i As Integer = 0 To t - 1
                If cmd.Old Then
                    Dim oldGameThread As New OldGameThread(i + 1, refereeCmd, playersCmd, count, playerStats, n, logs, swap)
                    oldGameThread.Start() ' Assuming OldGameThread inherits from Thread
                Else
                    Dim gameThread As New GameThread(i + 1, refereeCmd, playersCmd, count, playerStats, n, logs, swap)
                    gameThread.Start() ' Assuming GameThread inherits from Thread
                End If
            Next


        Catch exception As Exception
            LOG.Fatal("cg-brutaltester failed to start", exception)
            System.Environment.Exit(1)
        End Try

    End Sub

    Public Shared Sub finish()
        SyncLock playerStats
            finished += 1

            If finished >= t Then
                LOG.Info("*** End of games ***")
                playerStats.Print() ' Assuming PlayerStats has a print method
            End If
        End SyncLock
    End Sub

End Class


' Command-line options class (for CommandLineParser)
'<Verb("run", IsDefault:=True, HelpText:="Run the brutal tester")>
Public Class CommandLineOptions
    <Option("r", "referee", Required:=True, HelpText:="Referee command line")>
    Public Property Referee As String

    <Option("p1", "player1", Required:=True, HelpText:="Player 1 command line")>
    Public Property Player1 As String

    <Option("p2", "player2", Required:=True, HelpText:="Player 2 command line")>
    Public Property Player2 As String

    <Option("p3", "player3", HelpText:="Player 3 command line")>
    Public Property Player3 As String

    <Option("p4", "player4", HelpText:="Player 4 command line")>
    Public Property Player4 As String

    ' ... other options

    <Option("h", "help", HelpText:="Print the help")>
    Public Property Help As Boolean

    <Option("v", "verbose", HelpText:="Verbose mode")>
    Public Property Verbose As Boolean

    <Option("n", "games", DefaultValue:="1", HelpText:="Number of games to play")>
    Public Property Games As String

    <Option("t", "threads", DefaultValue:="1", HelpText:="Number of threads to spawn")>
    Public Property Threads As String

    <Option("l", "logs", HelpText:="Directory for game logs")>
    Public Property Logs As String

    <Option("s", "swap", HelpText:="Swap player positions")>
    Public Property Swap As Boolean

    <Option("i", "seed", HelpText:="Initial seed")>
    Public Property Seed As String

    <Option("o", "old", HelpText:="Old mode")>
    Public Property Old As Boolean


End Class
Public Class PlayerStats
    Private Const VICTORY As Integer = 0
    Private Const DEFEAT As Integer = 1
    Private Const DRAW As Integer = 2

    Private stats As Integer(,,)
    Private GlobalX As Integer(,)
    Private n As Integer
    Private total As Integer

    Public Sub New(n As Integer)
        Me.n = n
        total = 0
        stats = New Integer(n - 1, n - 1, 2) {} ' VB.NET array bounds are 0-based
        GlobalX = New Integer(n - 1, 2) {}       ' VB.NET array bounds are 0-based
    End Sub

    Public Sub Add(scores As Integer())
        SyncLock Me ' Equivalent to Java's synchronized
            For i As Integer = 0 To n - 1
                For j As Integer = i + 1 To n - 1
                    If scores(i) > scores(j) Then
                        stats(i, j, VICTORY) += 1
                        stats(j, i, DEFEAT) += 1
                        GlobalX(i, VICTORY) += 1
                        GlobalX(j, DEFEAT) += 1
                    ElseIf scores(i) < scores(j) Then
                        stats(j, i, VICTORY) += 1
                        stats(i, j, DEFEAT) += 1
                        GlobalX(j, VICTORY) += 1
                        GlobalX(i, DEFEAT) += 1
                    Else
                        stats(i, j, DRAW) += 1
                        stats(j, i, DRAW) += 1
                        GlobalX(i, DRAW) += 1
                        GlobalX(j, DRAW) += 1
                    End If
                Next
            Next

            total += 1
        End SyncLock
    End Sub

    Public Sub Add(line As String)
        Dim params As String() = line.Split(" "c) ' Split by space

        Dim positions As Integer() = New Integer(n - 1) {}

        For i As Integer = 1 To params.Length - 1
            For Each c As Char In params(i)
                positions(AscW(c) - AscW("0"c)) = i - 1 ' Convert char to int (assuming digits 0-9)
            Next
        Next

        SyncLock Me ' Equivalent to Java's synchronized
            For i As Integer = 0 To n - 1
                For j As Integer = i + 1 To n - 1
                    If positions(i) < positions(j) Then
                        stats(i, j, VICTORY) += 1
                        stats(j, i, DEFEAT) += 1
                        GlobalX(i, VICTORY) += 1
                        GlobalX(j, DEFEAT) += 1
                    ElseIf positions(i) > positions(j) Then
                        stats(j, i, VICTORY) += 1
                        stats(i, j, DEFEAT) += 1
                        GlobalX(j, VICTORY) += 1
                        GlobalX(i, DEFEAT) += 1
                    Else
                        stats(i, j, DRAW) += 1
                        stats(j, i, DRAW) += 1
                        GlobalX(i, DRAW) += 1
                        GlobalX(j, DRAW) += 1
                    End If
                Next
            Next

            total += 1
        End SyncLock
    End Sub

    Private Function Percent(amount As Single) As String
        Return String.Format("{0:P2}", amount / total) ' Format as percentage with 2 decimal places
    End Function


    Public Overrides Function ToString() As String
        Dim sb As New System.Text.StringBuilder()

        For i As Integer = 0 To n - 1
            sb.Append(" ").Append(Percent(GlobalX(i, VICTORY)))
        Next

        Return sb.ToString()
    End Function

    Public Sub Print()
        SyncLock Me ' Equivalent to Java's synchronized
            Dim separator As String = ""

            Select Case n
                Case 2 : separator = "+----------+----------+----------+"
                Case 3 : separator = "+----------+----------+----------+----------+"
                Case 4 : separator = "+----------+----------+----------+----------+----------+"
            End Select

            Console.WriteLine(separator)
            Console.Write("| Results  |")

            For i As Integer = 0 To n - 1
                Console.Write(" Player " & (i + 1) & " |")
            Next
            Console.WriteLine()
            Console.WriteLine(separator)

            For i As Integer = 0 To n - 1
                Console.Write("| Player " & (i + 1) & " |")

                For j As Integer = 0 To n - 1
                    Dim result As String = ""

                    If i <> j Then
                        result = Percent(stats(i, j, VICTORY))
                    End If

                    Console.Write(" " & result & "         ".Substring(0, 9 - result.Length) & "|")
                Next

                Console.WriteLine()
                Console.WriteLine(separator)
            Next
        End SyncLock
    End Sub

End Class
Public Class Mutable(Of T)
    Public Property Value As T

    Public Sub New(value As T)
        Me.Value = value
    End Sub

    Public Sub New()
        ' Default constructor - Value will be the default value for type T
    End Sub
End Class


Public Class SeedGenerator
    Private Shared random As New Random()
    Private Shared seed As Integer = 0
    Private Shared usedCount As Integer = 0
    Public Shared repeteableTests As Boolean = False

    Public Shared Sub initialSeed(newSeed As Long)
        random = New Random(CInt(newSeed)) ' Convert Long to Integer for Random constructor
        repeteableTests = True
    End Sub

    Public Shared Function nextSeed() As Integer
        SyncLock random ' Equivalent to Java's synchronized
            Return random.Next(0, Integer.MaxValue) ' Generates a random integer between 0 and Integer.MaxValue - 1
        End SyncLock
    End Function

    Public Shared Function getSeed(playerCount As Integer) As Integer()
        SyncLock random ' Equivalent to Java's synchronized
            usedCount = usedCount Mod playerCount
            If usedCount = 0 Then
                seed = random.Next(0, Integer.MaxValue) ' Generates a random integer between 0 and Integer.MaxValue - 1
            End If
            Dim result As Integer() = {seed, usedCount}
            usedCount += 1
            Return result
        End SyncLock
    End Function
End Class

Public Class BrutalProcess
    Private process As Process
    Private writer As StreamWriter
    Private reader As StreamReader
    Private errorReader As StreamReader

    Public Sub New(process As Process)
        Me.process = process
        writer = New StreamWriter(process.StandardInput.BaseStream, System.Text.Encoding.UTF8) ' Use UTF8 encoding
        reader = New StreamReader(process.StandardOutput.BaseStream, System.Text.Encoding.UTF8) ' Use UTF8 encoding
        errorReader = New StreamReader(process.StandardError.BaseStream, System.Text.Encoding.UTF8) ' Use UTF8 encoding
    End Sub

    Public Sub ClearErrorStream(thread As OldGameThread, prefix As String) ' Assuming OldGameThread has a log method
        While errorReader.Peek() <> -1 ' Check if there's data to read
            thread.log(prefix & errorReader.ReadLine())
        End While
    End Sub

    Public Sub Destroy()
        writer.Close()
        reader.Close()
        errorReader.Close()
        process.Kill() ' Use Kill instead of destroy
    End Sub

    Public Property Process1 As Process
        Get
            Return process
        End Get
        Set(value As Process)
            process = value
        End Set
    End Property

    Public ReadOnly Property Out As StreamWriter
        Get
            Return writer
        End Get
    End Property

    Public ReadOnly Property In As StreamReader
        Get
            Return reader
        End Get
    End Property

    Public ReadOnly Property Error As StreamReader
        Get
            Return errorReader
        End Get
    End Property
End Class
Imports com.magusgeek.brutaltester.util ' Assuming you have this namespace

Public Class GameThread
    Inherits Threading.Thread

    Private Shared ReadOnly LOG As NLog.Logger = NLog.LogManager.GetCurrentClassLogger()

    Private count As Mutable(Of Integer)
    Private stats As PlayerStats
    Private n As Integer
    Private referee As BrutalProcess
    Private logs As String
    Private game As Integer
    Private command As String()
    Private playersCount As Integer
    Private playersCmd As List(Of String)
    Private data As New System.Text.StringBuilder()
    Private swap As Boolean
    Private pArgIdx As Integer()
    Private refereeInputIdx As Integer

    Public Sub New(id As Integer, refereeCmd As String, playersCmd As List(Of String), count As Mutable(Of Integer), stats As PlayerStats, n As Integer, logs As String, swap As Boolean)
        MyBase.New("GameThread-" & id)
        Me.count = count
        Me.stats = stats
        Me.n = n
        Me.logs = logs
        Me.swap = swap
        Me.playersCount = playersCmd.Count
        Me.playersCmd = playersCmd
        pArgIdx = New Integer(playersCount - 1) {}
        Dim haveSeedArgs As Boolean = swap OrElse SeedGenerator.repeteableTests

        Dim splitted As String() = refereeCmd.Split(" "c)

        command = New String(splitted.Length + playersCount * 2 + (If(logs IsNot Nothing, 2, 0)) + (If(haveSeedArgs, 2, 0)) - 1) {}

        Array.Copy(splitted, command, splitted.Length) ' Copy splitted to command

        For i As Integer = 0 To playersCount - 1
            pArgIdx(i) = splitted.Length + i * 2 + 1
            command(splitted.Length + i * 2) = "-p" & (i + 1)
            command(splitted.Length + i * 2 + 1) = playersCmd(i)
        Next

        If haveSeedArgs Then
            Me.n *= playersCount
            refereeInputIdx = splitted.Length + playersCount * 2 + 1
            command(refereeInputIdx - 1) = "-d"
            command(refereeInputIdx) = ""
        End If

        If logs IsNot Nothing Then
            command(command.Length - 2) = "-l"
        End If
    End Sub

    Public Overrides Sub Run()
        While True
            game = 0
            SyncLock count
                If count.Value < n Then
                    game = count.Value + 1
                    count.Value = game
                End If
            End SyncLock

            If game = 0 Then
                ' End of this thread
                Main.finish()
                Exit While
            End If

            Try
                If logs IsNot Nothing Then
                    command(command.Length - 1) = logs & "/game" & game & ".json"
                End If

                Dim seedRotate As Integer() = SeedGenerator.getSeed(playersCount)
                If swap Then
                    command(refereeInputIdx) = "seed=" & seedRotate(0)
                    For i As Integer = 0 To playersCount - 1
                        command(pArgIdx(i)) = playersCmd((i + seedRotate(1)) Mod playersCount)
                    Next
                ElseIf SeedGenerator.repeteableTests Then
                    command(refereeInputIdx) = "seed=" & SeedGenerator.nextSeed()
                End If

                referee = New BrutalProcess(New Process() With {
                    .StartInfo = New ProcessStartInfo() With {
                        .FileName = command(0),
                        .Arguments = String.Join(" ", command.Skip(1)), ' Join arguments with spaces
                        .UseShellExecute = False,
                        .RedirectStandardInput = True,
                        .RedirectStandardOutput = True,
                        .RedirectStandardError = True,
                        .CreateNoWindow = True
                    }
                })
                referee.Process1.Start()


                Dim error As Boolean = False
                data.Clear()

                Dim scores As Integer() = New Integer(playersCount - 1) {}

                Dim fullOut As New System.Text.StringBuilder()

                Using inReader As StreamReader = referee.In
                    For pi As Integer = 0 To playersCount - 1
                        Dim i As Integer = If(swap, (pi + seedRotate(1)) Mod playersCount, pi)

                        If inReader.Peek() <> -1 Then
                            Dim line As String = inReader.ReadLine()
                            If Integer.TryParse(line, scores(i)) Then
                                ' Successfully parsed score
                            Else
                                fullOut.AppendLine(line)
                                While inReader.Peek() <> -1 AndAlso Not Integer.TryParse(inReader.ReadLine(), scores(i))
                                    fullOut.AppendLine(inReader.ReadLine())
                                End While
                            End If
                        End If


                        If scores(i) < 0 Then
                            Error = True
                            LOG.Error("Negative score during game " & game & " p" & i & ":" & scores(i))
                        End If
                    Next

                    While inReader.Peek() <> -1
                        data.AppendLine(inReader.ReadLine())
                    End While
                End Using

                If fullOut.Length > 0 Then
                    LOG.Error("Problem with referee output in game" & game & ". Output content:" & fullOut.ToString())
                End If

                If checkForError() Then
                    Error = True
                End If

                If error Then
                    logHelp()
                End If

                stats.Add(scores)

                LOG.Info("End of game " & game & vbTab & stats.ToString())

            Catch exception As Exception
                LOG.Error("Exception in game " & game, exception)
                logHelp()
            Finally
                destroyAll()
            End Try
        End While
    End Sub

    Private Sub logHelp()
        LOG.Error("If you want to replay and see this game, use the following command line:")

        If data.Length > 0 Then
            LOG.Error(String.Join(" ", command) & " -s -d " & data.ToString())
        Else
            LOG.Error(String.Join(" ", command) & " -s")
        End If
    End Sub

    Private Function checkForError() As Boolean
        Dim error As Boolean = False

        Using inReader As StreamReader = referee.Error
            If inReader.Peek() <> -1 Then
                Error = True
                LOG.Error("Error during game " & game)

                Dim sb As New System.Text.StringBuilder()
                While inReader.Peek() <> -1
                    sb.AppendLine(inReader.ReadLine())
                End While

                LOG.Error(sb.ToString())
            End If
        End Using

        Return error
    End Function

    Private Sub destroyAll()
        Try
            If referee IsNot Nothing Then
                referee.Destroy()
            End If
        Catch exception As Exception
            LOG.Error("Unable to destroy all")
        End Try
    End Sub
End Class
