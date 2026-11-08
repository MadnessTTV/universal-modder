using System;

namespace UmMegabonkStudio
{
    // Same edits as um/megabonk_prompt.py. No Unity text widget is involved.
    sealed class PromptBuffer
    {
        public string Text = "";
        public int Caret;
        public int Mark;

        public PromptBuffer() { }

        public PromptBuffer(string text)
        {
            Text = text ?? "";
            Caret = Mark = Text.Length;
        }

        public string SelectedText()
        {
            int start, end;
            Selection(out start, out end);
            if (end <= start) return "";
            return Text.Substring(start, end - start);
        }

        public void Insert(string value)
        {
            if (value == null) value = "";
            int start, end;
            Selection(out start, out end);
            Text = Text.Substring(0, start) + value + Text.Substring(end);
            Caret = Mark = start + value.Length;
        }

        public void Backspace()
        {
            if (Caret != Mark)
            {
                Insert("");
                return;
            }
            if (Caret <= 0) return;
            Text = Text.Substring(0, Caret - 1) + Text.Substring(Caret);
            Caret = Mark = Caret - 1;
        }

        public void Delete()
        {
            if (Caret != Mark)
            {
                Insert("");
                return;
            }
            if (Caret >= Text.Length) return;
            Text = Text.Substring(0, Caret) + Text.Substring(Caret + 1);
        }

        public void Move(string kind, bool extend, bool ctrl)
        {
            if (!extend && Caret != Mark && (kind == "left" || kind == "right") && !ctrl)
            {
                int start, end;
                Selection(out start, out end);
                Caret = Mark = kind == "left" ? start : end;
                return;
            }
            int nxt = Caret;
            if (kind == "left") nxt = ctrl ? WordLeft(Caret) : Caret - 1;
            else if (kind == "right") nxt = ctrl ? WordRight(Caret) : Caret + 1;
            else if (kind == "up") nxt = Vertical(Caret, -1);
            else if (kind == "down") nxt = Vertical(Caret, 1);
            else if (kind == "home") nxt = ctrl ? 0 : LineStart(Caret);
            else if (kind == "end") nxt = ctrl ? Text.Length : LineEnd(Caret);
            else return;
            Place(nxt, extend);
        }

        public void SelectAll()
        {
            Mark = 0;
            Caret = Text.Length;
        }

        public void SetCaret(int index, bool extend)
        {
            Place(index, extend);
        }

        public string Shown(bool focused, bool caretOn)
        {
            var text = Text ?? "";
            int start, end;
            Selection(out start, out end);
            if (start != end)
                return text.Substring(0, start) + "\u00AB" + text.Substring(start, end - start) + "\u00BB" + text.Substring(end);
            if (!focused || !caretOn) return text.Length == 0 ? " " : text;
            int c = Caret;
            if (c < 0) c = 0;
            if (c > text.Length) c = text.Length;
            return text.Substring(0, c) + "|" + text.Substring(c);
        }

        void Selection(out int start, out int end)
        {
            if (Caret < Mark) { start = Caret; end = Mark; }
            else { start = Mark; end = Caret; }
            if (start < 0) start = 0;
            if (end > Text.Length) end = Text.Length;
        }

        void Place(int index, bool extend)
        {
            if (index < 0) index = 0;
            if (index > Text.Length) index = Text.Length;
            Caret = index;
            if (!extend) Mark = Caret;
        }

        int LastNewlineBefore(int index)
        {
            if (index <= 0) return -1;
            return Text.LastIndexOf('\n', index - 1);
        }

        int LineStart(int index)
        {
            return LastNewlineBefore(index) + 1;
        }

        int LineEnd(int index)
        {
            int end = Text.IndexOf('\n', index);
            return end < 0 ? Text.Length : end;
        }

        int Vertical(int caret, int direction)
        {
            int start = LineStart(caret);
            int end = LineEnd(caret);
            int column = caret - start;
            if (direction < 0)
            {
                if (start == 0) return 0;
                int previousEnd = start - 1;
                int previousStart = LastNewlineBefore(previousEnd) + 1;
                return Math.Min(previousStart + column, previousEnd);
            }
            if (end >= Text.Length) return Text.Length;
            int nextStart = end + 1;
            int nextEnd = Text.IndexOf('\n', nextStart);
            if (nextEnd < 0) nextEnd = Text.Length;
            return Math.Min(nextStart + column, nextEnd);
        }

        int WordLeft(int caret)
        {
            int i = caret;
            if (i > 0 && Text[i - 1] == '\n') return i - 1;
            while (i > 0 && IsSpace(Text[i - 1]) && Text[i - 1] != '\n') i--;
            while (i > 0 && !IsSpace(Text[i - 1])) i--;
            return i;
        }

        int WordRight(int caret)
        {
            int i = caret;
            int n = Text.Length;
            if (i < n && Text[i] == '\n') return i + 1;
            while (i < n && !IsSpace(Text[i])) i++;
            while (i < n && IsSpace(Text[i]) && Text[i] != '\n') i++;
            return i;
        }

        static bool IsSpace(char ch)
        {
            return ch == ' ' || ch == '\t' || ch == '\r' || ch == '\n';
        }
    }
}
