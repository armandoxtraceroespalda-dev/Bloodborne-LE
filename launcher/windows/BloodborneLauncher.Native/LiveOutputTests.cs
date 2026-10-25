using System.Text;
using BloodborneLauncher.Native.Backend;

namespace BloodborneLauncher.Native;
internal static class LiveOutputTests
{
    public static void Run(string root)
    {
        void Check(bool yes, string text)
        {
            if (!yes)
                throw new InvalidOperationException(text);
            Console.WriteLine("PASS " + text);
        }

        string file = Path.Combine(root, "last_session.log");
        File.WriteAllText(file, "first\n", new UTF8Encoding(false));
        var output = new LiveOutputBuffer();
        Check(output.Poll(file) && output.Lines.SequenceEqual(new[] { "first" }), "live output reads the original backend session file");
        Check(!output.Poll(file), "unchanged output does not request a scene rebuild");
        using (var stream = new FileStream(file, FileMode.Append))
            stream.Write(new byte[] { 0xf0, 0x9f });
        output.Poll(file);
        Check(output.Lines.Count == 1, "incomplete UTF-8 output is not emitted as a broken line");
        using (var stream = new FileStream(file, FileMode.Append))
            stream.Write(new byte[] { 0x8c, 0x99, 10 });
        output.Poll(file);
        Check(output.Lines.Last() == "🌙", "the live decoder preserves Unicode split between polling batches");
        byte[] before = File.ReadAllBytes(file);
        output.Clear();
        Check(output.Lines.Count == 0 && File.ReadAllBytes(file).SequenceEqual(before), "Clear empties the displayed log without deleting saved diagnostics");
        Check(!output.Poll(file) && output.Lines.Count == 0, "cleared output is not replayed on the next idle poll");
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddSeconds(2));
        Check(!output.Poll(file) && output.Lines.Count == 0, "a metadata-only timestamp change cannot replay cleared output");
        File.AppendAllText(file, "after clear\n");
        output.Poll(file);
        Check(output.Lines.SequenceEqual(new[] { "after clear" }), "new output continues after Clear without restoring old history");
        File.WriteAllText(file, "new run\n");
        output.Poll(file);
        Check(output.Lines.SequenceEqual(new[] { "new run" }), "a truncated session log starts a fresh output run");
        string current = Path.Combine(root, "last_run.log");
        File.WriteAllText(current, "previous game output\n");
        var session = new LiveOutputBuffer(gameOnly: true, requireSession: true);
        Check(!session.Poll(current) && session.Text() == "", "opening the launcher never displays a previous game's output");
        session.BeginSession(new[] { current });
        Check(!session.Poll(current) && session.Text() == "", "pressing Play does not replay an unchanged saved game log");
        File.AppendAllText(current, "current game output\n");
        Check(session.Poll(current) && session.Lines.SequenceEqual(new[] { "current game output" }), "an appended game log displays only output written after Play");
        session.BeginSession(new[] { current });
        Check(session.Text() == "" && !session.Poll(current), "a second Play starts an empty output session without old messages");
        File.WriteAllText(current, "replacement game output\n");
        Check(session.Poll(current) && session.Lines.SequenceEqual(new[] { "replacement game output" }), "a recreated game log is read from its first current-session line");
        string delayed = Path.Combine(root, "delayed-run.log");
        session.BeginSession(new[] { delayed });
        Check(!session.Poll(delayed), "output remains empty while the game has not created its stream");
        File.WriteAllText(delayed, "first actual game line\n");
        Check(session.Poll(delayed) && session.Lines.SequenceEqual(new[] { "first actual game line" }), "a stream created after Play includes its first game message");
        Check(File.ReadAllText(current) == "replacement game output\n", "session boundaries never erase or rewrite saved game diagnostics");
        string longFile = Path.Combine(root, "many-lines.log");
        using (var writer = new StreamWriter(longFile, false, new UTF8Encoding(false)))
            for (int i = 0; i < 7000; i++)
                writer.WriteLine(i.ToString("D4") + new string ('x', 1020));
        var size = new FileInfo(longFile).Length;
        while (output.Position < size || output.PathRead != longFile)
            output.Poll(longFile);
        Check(output.Lines.Count <= LiveOutputBuffer.MaxLines && output.Lines.Sum(s => s.Length) <= LiveOutputBuffer.MaxCharacters, "log history remains bounded even while the engine produces megabytes of output");
        Check(output.Lines.Last().StartsWith("6999"), "bounded output retains the newest diagnostics");
        string oversized = Path.Combine(root, "oversized-line.log");
        File.WriteAllText(oversized, new string ('a', LiveOutputBuffer.MaxLine * 3) + "\nnext\n");
        output.Poll(oversized);
        Check(output.Lines.Count == 2 && output.Lines[0].Length == LiveOutputBuffer.MaxLine && output.Lines[1] == "next", "oversized output lines cannot grow the partial-line buffer without limit");
        var snapshot = output.Text();
        output.ReleaseSnapshot();
        Check(output.Text() == snapshot, "releasing the joined log snapshot preserves all displayed session lines");
        Check(snapshot.Contains("next"), "Copy and Export serialize the displayed output history");
    }
}
