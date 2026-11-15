using System.Text;

namespace PageCollector.Core.Tests;

[TestClass]
public sealed class SessionLogTests : TestWorkspace
{
    [TestMethod]
    public void CSVIsUTF8QuotedExcelSafeAndReadableBeforeTheSessionEnds()
    {
        using var log = new SessionLog(Path.Combine(Root, "logs"));
        log.Append(Candidate with { Value = "=1+1" }, "Accepted", "C:\\file.html", "a;\"b\"\nÄ");

        // Windows readers must allow the writer's existing access to the live log.
        using var reader = new FileStream(log.FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var snapshot = new MemoryStream();
        reader.CopyTo(snapshot);
        var bytes = snapshot.ToArray();

        Assert.IsTrue(bytes.Take(3).SequenceEqual(new byte[] { 0xef, 0xbb, 0xbf }));
        var contents = Encoding.UTF8.GetString(bytes);

        Assert.IsTrue(contents.Contains("\"'=1+1\""));
        Assert.IsTrue(contents.Contains("\"a;\"\"b\"\"\nÄ\""));
    }
}