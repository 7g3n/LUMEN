using Lumen.Core.Balance;
using Lumen.Core.Charts;

namespace Lumen.Core.Gameplay;

/// <summary>
/// The deterministic heart of a play (spec §15–25). Fed a song time and the lane events
/// since the last call, it advances every note, emits judgements, and updates the
/// <see cref="ScoreState"/>. No audio, no rendering, no RNG — the same chart and the same
/// event stream always produce the same result, which is what the replay system and the
/// unit tests rely on.
/// </summary>
public sealed class GameplaySession
{
    private readonly BalanceConfig _balance;
    private readonly JudgementWindows _w;
    private readonly List<NoteObject> _notes;
    private readonly NoteObject?[] _holding; // active hold per lane, or null

    private int _sweepCursor; // notes before this are all resolved

    public GameplaySession(Chart chart, BalanceConfig? balance = null)
    {
        _balance = balance ?? BalanceConfig.Default;
        _w = _balance.Windows;

        Chart = chart.Normalized();
        _notes = Chart.Notes.Select(n => new NoteObject(n)).ToList();
        _holding = new NoteObject?[Math.Max(1, Chart.LaneCount)];

        int units = Chart.TapCount + Chart.HoldCount * 2;
        Score = new ScoreState(units, _balance);
        Tempo = new TempoMap(Chart.BpmPoints);
    }

    public Chart Chart { get; }

    public TempoMap Tempo { get; }

    public ScoreState Score { get; }

    public IReadOnlyList<NoteObject> Notes => _notes;

    public double SongTimeMs { get; private set; }

    public bool AllResolved => _notes.All(n => n.Status == NoteStatus.Done);

    public event Action<JudgementEvent>? Judged;

    /// <summary>
    /// Advances to <paramref name="songTimeMs"/>, applying <paramref name="events"/>
    /// (which must be ordered by time and lie at or before <paramref name="songTimeMs"/>).
    /// </summary>
    public void Update(double songTimeMs, IReadOnlyList<LaneEvent> events)
    {
        for (int i = 0; i < events.Count; i++)
        {
            LaneEvent e = events[i];
            SweepMisses(e.TimeMs);
            ProcessEvent(e);
        }

        SweepMisses(songTimeMs);
        SongTimeMs = songTimeMs;
    }

    public void Update(double songTimeMs) => Update(songTimeMs, Array.Empty<LaneEvent>());

    /// <summary>Force-resolves everything still open. Call once when the song ends.</summary>
    public void Finish()
    {
        foreach (NoteObject note in _notes)
        {
            switch (note.Status)
            {
                case NoteStatus.Pending:
                    ResolveHeadMiss(note);
                    break;
                case NoteStatus.Holding:
                    // Held all the way to the end of the song: give the tail.
                    ApplyTail(note, Judgement.Perfect, 0);
                    break;
            }
        }
    }

    private void ProcessEvent(LaneEvent e)
    {
        if (e.Lane < 0 || e.Lane >= _holding.Length)
        {
            return;
        }

        if (e.IsDown)
        {
            ProcessPress(e);
        }
        else
        {
            ProcessRelease(e);
        }
    }

    private void ProcessPress(LaneEvent e)
    {
        NoteObject? target = EarliestPending(e.Lane);
        if (target is null)
        {
            return; // nothing to hit on this lane yet
        }

        double error = e.TimeMs - target.Note.TimeMs;
        if (!JudgementRule.InHitWindow(error, _w))
        {
            return; // ghost tap — no penalty in Phase 3
        }

        Judgement j = JudgementRule.ForTap(error, _w);
        target.HeadJudgement = j;
        target.HeadErrorMs = error;
        Score.Register(j);
        Emit(target, j, isTail: false, error);

        if (target.IsHold && j.IsHit())
        {
            target.Status = NoteStatus.Holding;
            _holding[e.Lane] = target;
        }
        else
        {
            target.Status = NoteStatus.Done;
            if (target.IsHold)
            {
                // Head missed outright -> the tail is lost too.
                ApplyTail(target, Judgement.Miss, 0, alreadyDone: true);
            }
        }
    }

    private void ProcessRelease(LaneEvent e)
    {
        NoteObject? held = _holding[e.Lane];
        if (held is null)
        {
            return;
        }

        _holding[e.Lane] = null;
        double endMs = held.Note.EndTimeMs;
        double error = e.TimeMs - endMs;

        Judgement tail = e.TimeMs < endMs - _w.HoldReleaseGraceMs
            ? Judgement.Miss                       // let go far too early
            : JudgementRule.ForHoldTail(error, _w);

        ApplyTail(held, tail, error);
    }

    private void SweepMisses(double uptoMs)
    {
        // Miss any pending head whose hit window has fully passed.
        while (_sweepCursor < _notes.Count)
        {
            NoteObject note = _notes[_sweepCursor];
            if (note.Status == NoteStatus.Done)
            {
                _sweepCursor++;
                continue;
            }

            if (note.Status == NoteStatus.Pending && note.Note.TimeMs < uptoMs - _w.HitMs)
            {
                ResolveHeadMiss(note);
                _sweepCursor++;
                continue;
            }

            break; // notes are time-sorted; nothing later can be missed yet
        }

        // Auto-resolve holds that were held past the tail window without a release.
        foreach (NoteObject? held in _holding)
        {
            if (held is { } h && h.Status == NoteStatus.Holding &&
                h.Note.EndTimeMs < uptoMs - _w.HoldTailMs)
            {
                _holding[h.Lane] = null;
                ApplyTail(h, Judgement.Perfect, 0);
            }
        }
    }

    private void ResolveHeadMiss(NoteObject note)
    {
        note.HeadJudgement = Judgement.Miss;
        note.Status = NoteStatus.Done;
        Score.Register(Judgement.Miss);
        Emit(note, Judgement.Miss, isTail: false, double.NaN);

        if (note.IsHold)
        {
            ApplyTail(note, Judgement.Miss, 0, alreadyDone: true);
        }
    }

    private void ApplyTail(NoteObject note, Judgement tail, double errorMs, bool alreadyDone = false)
    {
        note.TailJudgement = tail;
        if (!alreadyDone)
        {
            note.Status = NoteStatus.Done;
        }

        Score.Register(tail);
        Emit(note, tail, isTail: true, errorMs);
    }

    private NoteObject? EarliestPending(int lane)
    {
        foreach (NoteObject note in _notes)
        {
            if (note.Lane == lane && note.Status == NoteStatus.Pending)
            {
                return note;
            }
        }

        return null;
    }

    private void Emit(NoteObject note, Judgement j, bool isTail, double errorMs) =>
        Judged?.Invoke(new JudgementEvent(note, j, isTail, SongTimeMs, errorMs));
}
