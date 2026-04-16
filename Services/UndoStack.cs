using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PoSHBlox.Services;

/// <summary>
/// Simple lambda-based undo/redo stack. Every mutating operation calls
/// <see cref="Record"/> with the pair of actions that reverse and reapply
/// the change; <see cref="Undo"/> pops an entry and runs its undo action,
/// <see cref="Redo"/> pushes a popped entry back by running its redo action.
///
/// Design notes:
///   - Recording is suppressed while replaying so the reapply / reverse code
///     can call the same VM methods (AddConnection, etc.) without double-
///     recording the replayed operation.
///   - Any new Record clears the redo stack — standard.
///   - Labels are optional; exposed for a future undo-history UI.
/// </summary>
public partial class UndoStack : ObservableObject
{
    private readonly Stack<Entry> _undo = new();
    private readonly Stack<Entry> _redo = new();
    private bool _suppressed;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    /// <summary>
    /// Record a reversible operation. The caller has typically already applied
    /// the change; <paramref name="undo"/> reverses it, <paramref name="redo"/>
    /// reapplies it on a subsequent Redo. Calls during an active Undo / Redo
    /// are silently dropped to avoid double-recording replayed mutations.
    ///
    /// When <paramref name="coalesceKey"/> is non-null and the most recent
    /// entry on the stack shares the same key and was recorded within
    /// <see cref="CoalesceWindow"/>, the new change is merged into that entry
    /// — the existing undo action is kept (preserves the original starting
    /// state) and the redo action is replaced with the latest one. Typing a
    /// word into a parameter field produces a single undo entry instead of
    /// one per keystroke.
    /// </summary>
    public void Record(Action undo, Action redo, string label = "", string? coalesceKey = null)
    {
        if (_suppressed) return;

        if (coalesceKey != null && _undo.Count > 0)
        {
            var top = _undo.Peek();
            if (top.CoalesceKey == coalesceKey
                && (DateTime.UtcNow - top.Timestamp) < CoalesceWindow)
            {
                // Merge: retain the original undo (earliest state), swap in the
                // freshest redo (latest state). Refresh the timestamp so the
                // window slides with continued activity.
                _undo.Pop();
                _undo.Push(new Entry(top.Undo, redo, label, coalesceKey, DateTime.UtcNow));
                // Any new recording (coalesced or not) invalidates redo history.
                _redo.Clear();
                Notify();
                return;
            }
        }

        _undo.Push(new Entry(undo, redo, label, coalesceKey, DateTime.UtcNow));
        _redo.Clear();
        Notify();
    }

    /// <summary>
    /// How long consecutive same-key recordings are merged into one entry.
    /// Tuned for typing speed — 1 second of continuous activity collapses into
    /// a single undo step; a pause starts a fresh one.
    /// </summary>
    private static readonly TimeSpan CoalesceWindow = TimeSpan.FromSeconds(1);

    public void Undo()
    {
        if (_undo.Count == 0) return;
        var e = _undo.Pop();
        _suppressed = true;
        try { e.Undo(); }
        finally { _suppressed = false; }
        _redo.Push(e);
        Notify();
    }

    public void Redo()
    {
        if (_redo.Count == 0) return;
        var e = _redo.Pop();
        _suppressed = true;
        try { e.Redo(); }
        finally { _suppressed = false; }
        _undo.Push(e);
        Notify();
    }

    /// <summary>Wipe both stacks — called on project load / new graph.</summary>
    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        Notify();
    }

    /// <summary>
    /// Discard the most recent undo entry without applying its undo action.
    /// Used when a multi-step interaction (e.g. wire reroute) is canceled
    /// after its first step recorded — the recorded entry would otherwise
    /// show up in the user's undo history as a change they don't remember
    /// making.
    /// </summary>
    public void PopUndo()
    {
        if (_undo.Count > 0)
        {
            _undo.Pop();
            Notify();
        }
    }

    private void Notify()
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    private readonly record struct Entry(
        Action Undo,
        Action Redo,
        string Label,
        string? CoalesceKey = null,
        DateTime Timestamp = default);
}
