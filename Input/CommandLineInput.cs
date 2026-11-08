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
    private string _completedText;
    private string[] _completions;
    private string _completionBefore,_completionAfter;
    private int _completionIndex;
    private int _completedCaret;
    private string _suggestionText;
    private int _suggestionCaret=-1;
    private string[] _suggestions=Array.Empty<string>();
    public IReadOnlyList<string> Suggestions
    {
        get
        {
            if(_completions!=null && Text==_completedText && _caret==_completedCaret)return _completions;
            if(_suggestionText!=Text || _suggestionCaret!=_caret)
            {_suggestionText=Text;_suggestionCaret=_caret;_suggestions=CommandCompletion.Candidates(Text,_caret,out _,out _);}
            return _suggestions;
        }
    }
    public int SuggestionIndex=>_completions!=null && Text==_completedText && _caret==_completedCaret?_completionIndex:-1;
    public string Text { get; private set; } = "";
    public int Caret => _caret;
    public bool Selected => _selectAll;
    public void Open() { SetText(""); _historyIndex = _history.Count; _draft = Text; _openingKey = true; _completions=null;_completedText=null; }
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
        if(Pressed(Keys.Tab)){Complete(keyboard.IsKeyDown(Keys.LeftShift)||keyboard.IsKeyDown(Keys.RightShift));return;}
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
    internal void Complete(bool reverse=false)
    {
        if(Text!=_completedText || _caret!=_completedCaret || _completions==null)
        {
            _completions=CommandCompletion.Candidates(Text,_caret,out int start,out int end);
            _completionBefore=Text[..start];_completionAfter=Text[end..];_completionIndex=reverse?_completions.Length: -1;
        }
        if(_completions.Length==0)return;
        _completionIndex=(_completionIndex+(reverse?-1:1)+_completions.Length)%_completions.Length;
        string insertion=_completions[_completionIndex];
        string result=_completionBefore+insertion+_completionAfter;if(result.Length>MaximumLength)return;
        SetText(result);_caret=_completionBefore.Length+insertion.Length;_completedText=Text;_completedCaret=_caret;
    }
    private void Backspace()
    {
        if (_selectAll) SetText("");
        else if (_caret > 0) Text = Text.Remove(--_caret, 1);
    }
    private void SetText(string text) { Text = text; _caret = text.Length; _selectAll = false; }
}
