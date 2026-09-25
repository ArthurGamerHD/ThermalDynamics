using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml.Linq;
using Thermodynamics.Harness;

namespace Thermodynamics.Tests
{
    public static class AuthoredValues
    {
        public class Entry
        {
            public string TypeId;
            public string SubtypeId;
            public string Property;
            public float Value;

            public string Note;


            public override string ToString()
            {
                return TypeId + "/" + SubtypeId + " " + Property + " = "
                    + Value.ToString(CultureInfo.InvariantCulture);
            }
        }


        public static List<Entry> Read(params string[] properties)
        {

            List<Entry> found = new List<Entry>();

            string path = Path.Combine(ShippedBlocks.DataRoot(), "Cubes.xml");
            XDocument document = XDocument.Load(path, LoadOptions.PreserveWhitespace);

            foreach (XElement definition in document.Descendants("Definition"))
            {
                XElement id = definition.Element("Id");
                if (id == null) continue;


                string type = Text(id.Element("TypeId"));

                string subtype = Text(id.Element("SubtypeId"));

                foreach (XElement group in definition.Descendants("Group"))
                {
                    foreach (XElement element in group.Elements())
                    {
                        if (element.Name != "Decimal") continue;

                        string property = (string)element.Attribute("Name");
                        if (Array.IndexOf(properties, property) < 0) continue;

                        float value;
                        if (!float.TryParse((string)element.Attribute("Value"),
                            NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                        {
                            continue;
                        }


                        Entry entry = new Entry();
                        entry.TypeId = type;
                        entry.SubtypeId = subtype;
                        entry.Property = property;
                        entry.Value = value;

                        entry.Note = NoteAbove(element);
                        found.Add(entry);
                    }
                }
            }

            return found;
        }


        public static string NoteAbove(XElement element)
        {
            XNode node = element.PreviousNode;
            while (node != null)
            {
                XComment comment = node as XComment;
                if (comment != null) return Flatten(comment.Value);

                XText text = node as XText;
                if (text == null || !string.IsNullOrWhiteSpace(text.Value)) return "";

                node = node.PreviousNode;
            }

            return "";
        }


        private static string Flatten(string value)
        {
            if (value == null) return "";

            string[] lines = value.Split('\n');
            string joined = "";
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0) continue;
                joined = joined.Length == 0 ? line : joined + " " + line;
            }

            return joined;
        }


        private static string Text(XElement element)
        {
            return element == null ? "" : (element.Value ?? "").Trim();
        }


        public static string RepoRoot()
        {
            return Thermodynamics.Harness.ShippedBlocks.RepoRoot();
        }
    }
}
