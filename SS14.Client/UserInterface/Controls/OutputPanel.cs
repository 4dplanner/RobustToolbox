using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using SS14.Client.Graphics;
using SS14.Client.Graphics.Drawing;
using SS14.Client.Input;
using SS14.Client.Utility;
using SS14.Shared.Maths;
using SS14.Shared.Utility;
using Color = SS14.Shared.Maths.Color;

namespace SS14.Client.UserInterface.Controls
{
    /// <summary>
    ///     A control to handle output of message-by-message output panels, like the debug console and chat panel.
    /// </summary>
    public class OutputPanel : Control
    {
        private static readonly FormattedMessage.TagColor TagWhite = new FormattedMessage.TagColor(Color.White);
        private readonly List<Entry> _entries = new List<Entry>();
        private int _mouseWheelOffset;
        private int _totalContentHeight;
        private bool _isAtBottom = true;

        public void Clear()
        {
            _entries.Clear();
        }

        public void RemoveLine(int line)
        {
            var entry = _entries[line];
            _entries.RemoveAt(line);

            var font = _getFont();
            _totalContentHeight -= entry.Height + font.LineSeparation;
        }

        public void AddMessage(FormattedMessage message)
        {
            var entry = new Entry(message);

            _updateEntry(ref entry);

            _entries.Add(entry);
            var font = _getFont();
            _totalContentHeight += font.LineSeparation + entry.Height;
            if (_isAtBottom && ScrollFollowing)
            {
                _mouseWheelOffset = ScrollLimit;
            }
        }

        public bool ScrollFollowing { get; set; } = true;

        protected internal override void Draw(DrawingHandleScreen handle)
        {
            base.Draw(handle);

            var font = _getFont();
            var contentBox = UIBox2.FromDimensions(Vector2.Zero, Size);

            var entryOffset = 0;

            foreach (var entry in _entries)
            {
                if (entryOffset - _mouseWheelOffset < 0)
                {
                    entryOffset += entry.Height + font.LineSeparation;
                    continue;
                }

                if (entryOffset + entry.Height - _mouseWheelOffset > contentBox.Height)
                {
                    break;
                }
                // A stack for format tags.
                // This stack contains the format tag to RETURN TO when popped off.
                // So when a new color tag gets hit this stack gets the previous color pushed on.
                var formatStack = new Stack<FormattedMessage.Tag>(2);

                // The tag currently doing color.
                var currentColorTag = TagWhite;

                var globalBreakCounter = 0;
                var lineBreakIndex = 0;
                var baseLine = contentBox.TopLeft + new Vector2(0, font.Ascent + entryOffset - _mouseWheelOffset);
                foreach (var tag in entry.Message.Tags)
                {
                    switch (tag)
                    {
                        case FormattedMessage.TagColor tagColor:
                            formatStack.Push(currentColorTag);
                            currentColorTag = tagColor;
                            break;
                        case FormattedMessage.TagPop _:
                            var popped = formatStack.Pop();
                            switch (popped)
                            {
                                case FormattedMessage.TagColor tagColor:
                                    currentColorTag = tagColor;
                                    break;
                                default:
                                    throw new InvalidOperationException();
                            }
                            break;
                        case FormattedMessage.TagText tagText:
                        {
                            var text = tagText.Text;
                            for (var i = 0; i < text.Length; i++, globalBreakCounter++)
                            {
                                var chr = text[i];
                                if (lineBreakIndex < entry.LineBreaks.Count &&
                                    entry.LineBreaks[lineBreakIndex] == globalBreakCounter)
                                {
                                    baseLine = new Vector2(contentBox.Left, baseLine.Y + font.LineHeight);
                                    lineBreakIndex += 1;
                                }

                                var advance = font.DrawChar(handle, chr, baseLine, currentColorTag.Color);
                                baseLine += new Vector2(advance, 0);
                            }

                            break;
                        }
                    }
                }

                entryOffset += entry.Height + font.LineSeparation;
            }
        }

        protected internal override void MouseWheel(GUIMouseWheelEventArgs args)
        {
            base.MouseWheel(args);

            if (args.WheelDirection == Mouse.Wheel.Up)
            {
                _mouseWheelOffset = Math.Max(0, _mouseWheelOffset - 10);
                _isAtBottom = false;
            }
            else if (args.WheelDirection == Mouse.Wheel.Down)
            {
                var limit = ScrollLimit;
                _mouseWheelOffset = Math.Min(_mouseWheelOffset + 10, limit);
                if (limit == _mouseWheelOffset)
                {
                    _isAtBottom = true;
                }
            }
        }

        private int ScrollLimit => _totalContentHeight - (int)Size.Y + 1;

        /// <summary>
        ///     Recalculate line dimensions and where it has line breaks for word wrapping.
        /// </summary>
        private void _updateEntry(ref Entry entry)
        {
            // This method is gonna suck due to complexity.
            // Bear with me here.
            // I am so deeply sorry for the person adding stuff to this in the future.
            var font = _getFont();
            var contentBox = UIBox2.FromDimensions(Vector2.Zero, Size);
            // Horizontal size we have to work with here.
            var sizeX = contentBox.Width;
            entry.Height = font.Height;
            entry.LineBreaks.Clear();

            // Index we put into the LineBreaks list when a line break should occur.
            var breakIndexCounter = 0;
            // If the CURRENT processing word ends up too long, this is the index to put a line break.
            int? wordStartBreakIndex = null;
            // Word size in pixels.
            var wordSizePixels = 0;
            // The horizontal position of the text cursor.
            var posX = 0;
            var lastChar = 'A';
            // If a word is larger than sizeX, we split it.
            // We need to keep track of some data to split it into two words.
            (int breakIndex, int wordSizePixels)? forceSplitData = null;
            // Go over every text tag.
            // We treat multiple text tags as one continuous one.
            // So changing color inside a single word doesn't create a word break boundary.
            foreach (var tag in entry.Message.Tags)
            {
                if (!(tag is FormattedMessage.TagText tagText))
                {
                    // TODO: We definitely will have to support other tags eventually.
                    // for example bold text changes glyph metrics.
                    // For now color is pretty irrelevant though. Yay!
                    continue;
                }

                var text = tagText.Text;
                // And go over every character.
                for (var i = 0; i < text.Length; i++, breakIndexCounter++)
                {
                    var chr = text[i];

                    if (IsWordBoundary(lastChar, chr) || chr == '\n')
                    {
                        // Word boundary means we know where the word ends.
                        if (posX > sizeX)
                        {
                            DebugTools.Assert(wordStartBreakIndex.HasValue,
                                "wordStartBreakIndex can only be null if the word begins at a new line, in which case this branch shouldn't be reached as the word would be split due to being longer than a single line.");
                            // We ran into a word boundary and the word is too big to fit the previous line.
                            // So we insert the line break BEFORE the last word.
                            entry.LineBreaks.Add(wordStartBreakIndex.Value);
                            entry.Height += font.LineHeight;
                            posX = wordSizePixels;
                        }

                        // Start a new word since we hit a word boundary.
                        //wordSize = 0;
                        wordSizePixels = 0;
                        wordStartBreakIndex = breakIndexCounter;
                        forceSplitData = null;

                        // Just manually handle newlines.
                        if (chr == '\n')
                        {
                            entry.LineBreaks.Add(breakIndexCounter);
                            entry.Height += font.LineHeight;
                            posX = 0;
                            lastChar = chr;
                            wordStartBreakIndex = null;
                            continue;
                        }
                    }

                    // Uh just skip unknown characters I guess.
                    if (!font.TryGetCharMetrics(chr, out var metrics))
                    {
                        lastChar = chr;
                        continue;
                    }

                    // Increase word size and such with the current character.
                    var oldWordSizePixels = wordSizePixels;
                    wordSizePixels += metrics.Advance;
                    // TODO: Theoretically, does it make sense to break after the glyph's width instead of its advance?
                    //   It might result in some more tight packing but I doubt it'd be noticeable.
                    //   Also definitely even more complex to implement.
                    posX += metrics.Advance;

                    if (posX > sizeX)
                    {
                        if (!forceSplitData.HasValue)
                        {
                            // If this character put us over the sizeX,
                            // this is where we'll break if the word itself ends up being larger than sizeX.
                            // Also keep track of the word size of the new split word so
                            // we can re-calculate the size of the second part of the split.
                            forceSplitData = (breakIndexCounter, oldWordSizePixels);
                        }

                        // Oh hey we get to break a word that doesn't fit on a single line.
                        if (wordSizePixels > sizeX)
                        {
                            var (breakIndex, splitWordSize) = forceSplitData.Value;
                            if (splitWordSize == 0)
                            {
                                // Uh I'm just gonna bail as there's clearly too little room to reasonably render this.
                                // This happens when a single glyph is too large for a line which...
                                // If somebody wants to "improve" this so it renders the glyph by going over size,
                                // fine by me. As long as it doesn't crash.
                                return;
                            }

                            // Reset forceSplitData so that we can split again if necessary.
                            forceSplitData = null;
                            entry.LineBreaks.Add(breakIndex);
                            entry.Height += font.LineHeight;
                            wordSizePixels -= splitWordSize;
                            wordStartBreakIndex = null;
                            posX = wordSizePixels;
                        }
                    }

                    lastChar = chr;
                }
            }

            // This needs to happen because word wrapping doesn't get checked for the last word.
            if (posX > sizeX)
            {
                DebugTools.Assert(wordStartBreakIndex.HasValue,
                    "wordStartBreakIndex can only be null if the word begins at a new line, in which case this branch shouldn't be reached as the word would be split due to being longer than a single line.");
                entry.LineBreaks.Add(wordStartBreakIndex.Value);
                entry.Height += font.LineHeight;
            }
        }

        protected override void Resized()
        {
            base.Resized();

            _invalidateEntries();
        }

        private void _invalidateEntries()
        {
            _totalContentHeight = 0;
            var font = _getFont();
            for (var i = 0; i < _entries.Count; i++)
            {
                var entry = _entries[i];
                _updateEntry(ref entry);
                _entries[i] = entry;
                _totalContentHeight += entry.Height + font.LineSeparation;
            }

            if (_isAtBottom && ScrollFollowing)
            {
                _mouseWheelOffset = ScrollLimit;
            }
        }

        [Pure]
        private static bool IsWordBoundary(char a, char b)
        {
            return a == ' ' || b == ' ' || a == '-' || b == '-';
        }

        [Pure]
        private Font _getFont()
        {
            if (TryGetStyleProperty("font", out Font font))
            {
                return font;
            }

            return UserInterfaceManager.ThemeDefaults.DefaultFont;
        }

        private struct Entry
        {
            public readonly FormattedMessage Message;

            /// <summary>
            ///     The size of this line, in pixels.
            /// </summary>
            public int Height;

            /// <summary>
            ///     The combined text indices in the message's text tags to put line breaks.
            /// </summary>
            public readonly List<int> LineBreaks;

            public Entry(FormattedMessage message)
            {
                Message = message;
                Height = 0;
                LineBreaks = new List<int>();
            }
        }
    }
}