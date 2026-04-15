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
    /// </summary>
    public void Record(Action undo, Action redo, string label = "")
    {
        if (_suppressed) return;
        _undo.Push(new Entry(undo, redo, label));
        _redo.Clear();
        Notify();
    }

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

    private readonly record struct Entry(Action Undo, Action Redo, string Label);
}
