using System;
using VRageMath;

namespace RichHudFramework.UI.Rendering
{
    [Flags]
    public enum FontStyles : int
    {
        Regular = 0,

        Bold = 1,

        Italic = 2,

		BoldItalic = 3,

        Underline = 4
    }

    public interface IFontMin
    {
        string Name { get; }

        int Index { get; }

        float PtSize { get; }

        float BaseScale { get; }

        Vector2I Regular { get; }

        Vector2I Bold { get; }

        Vector2I Italic { get; }

        Vector2I Underline { get; }

        Vector2I BoldItalic { get; }

        Vector2I BoldUnderline { get; }

        Vector2I BoldItalicUnderline { get; }


        bool IsStyleDefined(FontStyles styleEnum);


        bool IsStyleDefined(int style);


        Vector2I GetStyleIndex(int style);


        Vector2I GetStyleIndex(FontStyles style);
    }
}