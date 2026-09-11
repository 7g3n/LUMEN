using System.Text.Json.Nodes;

namespace Lumen.Core.Charts;

/// <summary>
/// Brings a chart document up to the current format (spec §98).
///
/// Migration works on the JSON document rather than on a typed object, because that is
/// what a format change actually is: fields move, get renamed, or have to be derived from
/// what the old version did record. A typed reader can only tolerate missing fields; it
/// cannot rebuild one.
///
/// Steps are applied in order and each one only has to understand the version before it,
/// so adding v3 later means appending one function - a v1 file still opens because it is
/// walked through every step in turn.
/// </summary>
public static class ChartMigrator
{
    /// <summary>The oldest format this build can still read.</summary>
    public const int OldestSupportedVersion = 1;

    private delegate void Step(JsonObject document);

    /// <summary>`Steps[n]` upgrades a document from version n to n + 1.</summary>
    private static readonly IReadOnlyDictionary<int, Step> Steps = new Dictionary<int, Step>
    {
        [1] = V1ToV2,
    };

    public static bool CanRead(int formatVersion) =>
        formatVersion >= OldestSupportedVersion && formatVersion <= GameIdentity.ChartFormatVersion;

    /// <summary>
    /// Upgrades <paramref name="document"/> in place and returns it. Throws when the file
    /// is from a build newer than this one - guessing at a format we have never seen
    /// would be worse than saying so.
    /// </summary>
    public static JsonObject Migrate(JsonObject document)
    {
        int version = ReadVersion(document);

        if (version > GameIdentity.ChartFormatVersion)
        {
            throw new FormatException(
                $"This chart was made with a newer version of {GameIdentity.Name} " +
                $"(chart format v{version}; this build reads up to " +
                $"v{GameIdentity.ChartFormatVersion}).");
        }

        if (version < OldestSupportedVersion)
        {
            throw new FormatException(
                $"Chart format v{version} is no longer supported.");
        }

        while (version < GameIdentity.ChartFormatVersion)
        {
            if (!Steps.TryGetValue(version, out Step? step))
            {
                // No step registered means nothing changed at that boundary; stamping the
                // version forward keeps the loop honest rather than spinning.
                break;
            }

            step(document);
            version++;
        }

        document["formatVersion"] = GameIdentity.ChartFormatVersion;
        return document;
    }

    private static int ReadVersion(JsonObject document) =>
        document.TryGetPropertyValue("formatVersion", out JsonNode? node)
        && node is not null
        && int.TryParse(node.ToString(), out int version)
            ? version
            : 1; // A document with no stamp predates the field, so it is v1.

    /// <summary>
    /// v1 → v2: metadata grew a description, tags, a cover reference and the audio length.
    ///
    /// The first three are simply absent in v1 and default cleanly, but the length is
    /// information v1 files do carry - implicitly, as the time of their last note. Deriving
    /// it here is the difference between an imported v1 chart showing a sensible duration
    /// in Song Select and showing "0:00".
    /// </summary>
    private static void V1ToV2(JsonObject document)
    {
        if (document["meta"] is not JsonObject meta)
        {
            meta = new JsonObject();
            document["meta"] = meta;
        }

        meta["description"] ??= "";
        meta["tags"] ??= new JsonArray();
        if (!meta.ContainsKey("coverFile"))
        {
            meta["coverFile"] = null;
        }

        double existing = meta["durationMs"] is { } d && double.TryParse(d.ToString(), out double value)
            ? value
            : 0;

        if (existing <= 0)
        {
            meta["durationMs"] = LastNoteMs(document);
        }
    }

    private static double LastNoteMs(JsonObject document)
    {
        if (document["notes"] is not JsonArray notes)
        {
            return 0;
        }

        double last = 0;
        foreach (JsonNode? node in notes)
        {
            if (node is not JsonObject note)
            {
                continue;
            }

            last = Math.Max(last, Number(note, "ms"));
            last = Math.Max(last, Number(note, "endMs"));
        }

        return last;
    }

    private static double Number(JsonObject node, string property) =>
        node[property] is { } value && double.TryParse(value.ToString(), out double parsed)
            ? parsed
            : 0;
}
