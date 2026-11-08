using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework.Input;

namespace MDSM.Input;

// Bounded single-line editor; text comes from the window's real text-input events.
internal sealed class CommandLineInput
{
    public const int MaximumLength = 256;
    private readonly List<string> _history = new();
    private int _historyIndex, _caret;
    private string _draft = "";
    private bool _selectAll, _openingKey;
    public string Text { get; private set; } = "";
    public int Caret => _caret;
    public bool Selected => _selectAll;
    public void Open() { SetText(""); _historyIndex = _history.Count; _draft = Text; _openingKey = true; }
    public void TextInput(char character)
    {
        if (_openingKey && Text.Length == 0 && character is 't' or 'T') return; // Ignore the opening key and its repeats, not T in a command.
        if (character == '\b') { Backspace(); return; }
        if (character < ' ' || character > '~') return;
        if (!_selectAll && Text.Length >= MaximumLength) return;
        if (_selectAll) { Text = ""; _caret = 0; _selectAll = false; }
        Text = Text.Insert(_caret++, character.ToString());
    }
    public void Update(KeyboardState keyboard, KeyboardState previous)
    {
        if (keyboard.IsKeyUp(Keys.T)) _openingKey = false;
        bool Pressed(Keys key) => keyboard.IsKeyDown(key) && previous.IsKeyUp(key);
        if ((keyboard.IsKeyDown(Keys.LeftControl) || keyboard.IsKeyDown(Keys.RightControl)) && Pressed(Keys.A)) { _selectAll = true; return; }
        if (Pressed(Keys.Home)) { _caret = 0; _selectAll = false; }
        if (Pressed(Keys.End)) { _caret = Text.Length; _selectAll = false; }
        if (Pressed(Keys.Left)) { _caret = _selectAll ? 0 : Math.Max(0, _caret - 1); _selectAll = false; }
        if (Pressed(Keys.Right)) { _caret = _selectAll ? Text.Length : Math.Min(Text.Length, _caret + 1); _selectAll = false; }
        if (Pressed(Keys.Delete))
        {
            if (_selectAll) SetText("");
            else if (_caret < Text.Length) Text = Text.Remove(_caret, 1);
        }
        if (Pressed(Keys.Up) && _historyIndex > 0)
        { if (_historyIndex == _history.Count) _draft = Text; SetText(_history[--_historyIndex]); }
        if (Pressed(Keys.Down) && _historyIndex < _history.Count)
        { _historyIndex++; SetText(_historyIndex == _history.Count ? _draft : _history[_historyIndex]); }
    }
    public string Submit()
    {
        string line = Text.Trim();
        if (_history.Count == 0 || _history[^1] != line)
        { if (_history.Count == 32) _history.RemoveAt(0); _history.Add(line); }
        SetText(""); _draft = Text; _historyIndex = _history.Count;
        return line;
    }
    public void ClearHistory() { _history.Clear(); Open(); }
    private void Backspace()
    {
        if (_selectAll) SetText("");
        else if (_caret > 0) Text = Text.Remove(--_caret, 1);
    }
    private void SetText(string text) { Text = text; _caret = text.Length; _selectAll = false; }
}
