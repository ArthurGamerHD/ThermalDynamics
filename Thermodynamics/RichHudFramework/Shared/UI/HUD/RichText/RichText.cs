using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using VRage;
using GlyphFormatMembers = VRage.MyTuple<byte, float, VRageMath.Vector2I, VRageMath.Color>;

namespace RichHudFramework
{
    using RichStringMembers = MyTuple<StringBuilder, GlyphFormatMembers>;

    namespace UI
    {
        public class RichText : IEnumerable<RichStringMembers>, IEquatable<RichText>
        {
            public GlyphFormat? defaultFormat;

            public readonly List<RichStringMembers> apiData;

            private ObjectPool<StringBuilder> sbPool;

/// <summary>RichText operation.</summary>
            public RichText(GlyphFormat? defaultFormat = null)
            {
                this.defaultFormat = defaultFormat ?? GlyphFormat.Empty;
/// <summary>List operation.</summary>
                apiData = new List<RichStringMembers>();
            }

/// <summary>RichText operation.</summary>
            public RichText(List<RichStringMembers> apiData, bool copy = false)
            {
/// <summary>Returns the datacopy.</summary>
                this.apiData = copy ? GetDataCopy(apiData) : apiData;
                defaultFormat = GlyphFormat.Empty;
            }

/// <summary>RichText operation.</summary>
            public RichText(RichText original)
            {
/// <summary>List operation.</summary>
                apiData = new List<RichStringMembers>();
                defaultFormat = original.defaultFormat;
                Add(original);
            }

/// <summary>RichText operation.</summary>
            public RichText(string text, GlyphFormat? defaultFormat = null)
            {
                this.defaultFormat = defaultFormat ?? GlyphFormat.Empty;
/// <summary>List operation.</summary>
                apiData = new List<RichStringMembers>();
                apiData.Add(new RichStringMembers(new StringBuilder(text), this.defaultFormat.Value.Data));
            }

/// <summary>RichText operation.</summary>
            public RichText(StringBuilder text, GlyphFormat? defaultFormat = null)
            {
                this.defaultFormat = defaultFormat ?? GlyphFormat.Empty;
/// <summary>List operation.</summary>
                apiData = new List<RichStringMembers>();
                Add(text);
            }

/// <summary>Returns the enumerator.</summary>
            public IEnumerator<RichStringMembers> GetEnumerator() => apiData.GetEnumerator();

            IEnumerator IEnumerable.GetEnumerator() => apiData.GetEnumerator();

/// <summary>Adds a .</summary>
            public void Add(RichText text)
            {
                if (sbPool == null)
/// <summary>ObjectPool operation.</summary>
                    sbPool = new ObjectPool<StringBuilder>(new StringBuilderPoolPolicy());

                List<RichStringMembers> currentStrings = apiData,
                    newStrings = text.apiData;

                if (newStrings.Count > 0)
                {
                    int index = 0, end = newStrings.Count - 1;

                    if (currentStrings.Count > 0)
                    {
                        GlyphFormatMembers newFormat = newStrings[0].Item2;
                        StringBuilder sb;
                        bool formatEqual;

                        GetNextStringBuilder(newFormat, out sb, out formatEqual);

                        if (formatEqual)
                        {
                            StringBuilder newSb = newStrings[0].Item1;
                            sb.EnsureCapacity(sb.Length + newSb.Length);

                            for (int i = 0; i < newSb.Length; i++)
                                sb.Append(newSb[i]);

                            index++;
                        }
                    }

                    for (int i = index; i <= end; i++)
                    {
                        StringBuilder sb = sbPool.Get(),
                            newSb = newStrings[i].Item1;

                        sb.EnsureCapacity(sb.Length + newSb.Length);
                        currentStrings.Add(new RichStringMembers(sb, newStrings[i].Item2));

                        for (int j = 0; j < newSb.Length; j++)
                            sb.Append(newSb[j]);
                    }
                }
            }

/// <summary>Adds a .</summary>
            public void Add(StringBuilder text, GlyphFormat? newFormat = null)
            {
                if (sbPool == null)
/// <summary>ObjectPool operation.</summary>
                    sbPool = new ObjectPool<StringBuilder>(new StringBuilderPoolPolicy());

                List<RichStringMembers> richStrings = apiData;
                GlyphFormatMembers format = newFormat?.Data ?? defaultFormat?.Data ?? GlyphFormat.Empty.Data;
                StringBuilder sb;
                bool formatEqual;

                GetNextStringBuilder(newFormat?.Data ?? GlyphFormat.Empty.Data, out sb, out formatEqual);

                if (!formatEqual)
                {
/// <summary>RichStringMembers operation.</summary>
                    var richString = new RichStringMembers(sb, format);
                    richStrings.Add(richString);
                }

                sb.EnsureCapacity(sb.Length + text.Length);

                for (int i = 0; i < text.Length; i++)
                    sb.Append(text[i]);
            }

/// <summary>Adds a .</summary>
            public void Add(GlyphFormat newFormat, StringBuilder text) =>
                Add(text, newFormat);

/// <summary>Adds a .</summary>
            public void Add(string text, GlyphFormat? newFormat = null)
            {
                if (sbPool == null)
/// <summary>ObjectPool operation.</summary>
                    sbPool = new ObjectPool<StringBuilder>(new StringBuilderPoolPolicy());

                List<RichStringMembers> richStrings = apiData;
                GlyphFormatMembers format = newFormat?.Data ?? defaultFormat?.Data ?? GlyphFormat.Empty.Data;
                StringBuilder sb;
                bool formatEqual;

                GetNextStringBuilder(newFormat?.Data ?? GlyphFormat.Empty.Data, out sb, out formatEqual);

                if (!formatEqual)
                {
/// <summary>RichStringMembers operation.</summary>
                    var richString = new RichStringMembers(sb, format);
                    richStrings.Add(richString);
                }

                sb.Append(text);
            }

/// <summary>Adds a .</summary>
            public void Add(GlyphFormat newFormat, string text) => Add(text, newFormat);

/// <summary>Adds a .</summary>
            public void Add(char ch, GlyphFormat? newFormat = null)
            {
                if (sbPool == null)
/// <summary>ObjectPool operation.</summary>
                    sbPool = new ObjectPool<StringBuilder>(new StringBuilderPoolPolicy());

                List<RichStringMembers> richStrings = apiData;
                GlyphFormatMembers format = newFormat?.Data ?? defaultFormat?.Data ?? GlyphFormat.Empty.Data;
                StringBuilder sb;
                bool formatEqual;

                GetNextStringBuilder(newFormat?.Data ?? GlyphFormat.Empty.Data, out sb, out formatEqual);

                if (!formatEqual)
                {
/// <summary>RichStringMembers operation.</summary>
                    var richString = new RichStringMembers(sb, format);
                    richStrings.Add(richString);
                }

                sb.Append(ch);
            }

/// <summary>Returns the nextstringbuilder.</summary>
            private void GetNextStringBuilder(GlyphFormatMembers newFormat, out StringBuilder sb, out bool formatEqual)
            {
                List<RichStringMembers> richStrings = apiData;
                int last = richStrings.Count - 1;
                formatEqual = false;

                if (richStrings.Count > 0)
                {
                    GlyphFormatMembers lastFormat = richStrings[last].Item2;
                    formatEqual = newFormat.Item1 == lastFormat.Item1
                        && newFormat.Item2 == lastFormat.Item2
                        && newFormat.Item3 == lastFormat.Item3
                        && newFormat.Item4 == lastFormat.Item4;
                }

                sb = formatEqual ? richStrings[last].Item1 : sbPool.Get();
            }

/// <summary>TrimExcess operation.</summary>
            public void TrimExcess()
            {
                if (sbPool == null)
/// <summary>ObjectPool operation.</summary>
                    sbPool = new ObjectPool<StringBuilder>(new StringBuilderPoolPolicy());

                List<RichStringMembers> text = apiData;

                for (int n = 0; n < text.Count; n++)
                    text[n].Item1.Capacity = text[n].Item1.Length;

                sbPool.TrimExcess();
                text.TrimExcess();
            }

/// <summary>Clear operation.</summary>
            public void Clear()
            {
                if (sbPool == null)
/// <summary>ObjectPool operation.</summary>
                    sbPool = new ObjectPool<StringBuilder>(new StringBuilderPoolPolicy());

                List<RichStringMembers> text = apiData;
                sbPool.ReturnRange(text, 0, text.Count);
                text.Clear();
            }

/// <summary>Returns the hashcode.</summary>
            public override int GetHashCode()
            {
                return base.GetHashCode();
            }

/// <summary>Equals operation.</summary>
            public override bool Equals(object obj)
            {
                RichText other = obj as RichText;

                if (apiData == other?.apiData)
                    return true;
                if (other != null)
/// <summary>Equals operation.</summary>
                    return Equals(other);
                else
                    return false;
            }

/// <summary>Equals operation.</summary>
            public bool Equals(RichText other)
            {
                bool isFormatEqual = true,
                    isTextEqual = true,
                    isLengthEqual = true;

                if (other == null)
                    return false;
/// <summary>if operation.</summary>
                else if (apiData == other.apiData)
                    return true;
/// <summary>if operation.</summary>
                else if (apiData.Count == other.apiData.Count)
                {
                    for (int i = 0; i < apiData.Count; i++)
                    {
                        if (apiData[i].Item1.Length != other.apiData[i].Item1.Length)
                        {
                            isLengthEqual = false;
                            break;
                        }
                    }

                    if (isLengthEqual)
                    {
                        for (int i = 0; i < apiData.Count; i++)
                        {
                            GlyphFormatMembers fmt = apiData[i].Item2,
                                otherFmt = other.apiData[i].Item2;

                            if (fmt.Item1 != otherFmt.Item1 ||
                                fmt.Item2 != otherFmt.Item2 ||
                                fmt.Item3 != otherFmt.Item3 ||
                                fmt.Item4 != otherFmt.Item4)
                            {
                                isFormatEqual = false;
                                break;
                            }
                        }
                    }
                    else
                        isFormatEqual = false;

                    if (isFormatEqual)
                    {
                        for (int i = 0; i < apiData.Count; i++)
                        {
                            for (int j = 0; j < apiData[i].Item1.Length; j++)
                            {
                                if (apiData[i].Item1[j] != other.apiData[i].Item1[j])
                                {
                                    isTextEqual = false;
                                    break;
                                }
                            }
                        }
                    }
                }
                else
                    isLengthEqual = false;

                return isLengthEqual && isFormatEqual && isTextEqual;
            }

/// <summary>ToString operation.</summary>
            public override string ToString()
            {
/// <summary>StringBuilder operation.</summary>
                StringBuilder rawText = new StringBuilder();
                List<RichStringMembers> richText = apiData;
                int charCount = 0;

                for (int i = 0; i < richText.Count; i++)
                    charCount += richText[i].Item1.Length;

                rawText.EnsureCapacity(charCount);

                for (int i = 0; i < richText.Count; i++)
                {
                    for (int b = 0; b < richText[i].Item1.Length; b++)
                        rawText.Append(richText[i].Item1[b]);
                }

                return rawText.ToString();
            }

/// <summary>Returns the copy.</summary>
            public RichText GetCopy() =>
/// <summary>RichText operation.</summary>
                new RichText(GetDataCopy(apiData));

/// <summary>Returns the datacopy.</summary>
            public static List<RichStringMembers> GetDataCopy(List<RichStringMembers> original)
            {
/// <summary>List operation.</summary>
                var newData = new List<RichStringMembers>(original.Count);

                for (int i = 0; i < original.Count; i++)
                {
                    StringBuilder oldSb = original[i].Item1,
/// <summary>StringBuilder operation.</summary>
                        sb = new StringBuilder(oldSb.Length);

                    for (int j = 0; j < oldSb.Length; j++)
                        sb.Append(oldSb[j]);

                    newData.Add(new RichStringMembers(sb, original[i].Item2));
                }

                return newData;
            }

            #region Operators

            public static RichText operator +(RichText left, string right)
            {
                left.Add(right);
                return left;
            }

            public static RichText operator +(RichText left, StringBuilder right)
            {
                left.Add(right);
                return left;
            }

            public static RichText operator +(RichText left, RichText right)
            {
                left.Add(right);
                return left;
            }

/// <summary>RichText operation.</summary>
            public static implicit operator RichText(string text) => new RichText(text);

/// <summary>RichText operation.</summary>
            public static implicit operator RichText(StringBuilder text) => new RichText(text);

/// <summary>RichText operation.</summary>
            public static implicit operator RichText(List<RichStringMembers> text) => new RichText(text);

            #endregion
        }
    }
}