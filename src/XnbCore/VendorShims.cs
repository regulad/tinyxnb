using System;

// The vendored MonoGame decompressors expect one type from the framework proper.
// Supplying it here keeps Vendor/ byte-identical to upstream, so it can be re-fetched
// or diffed against MonoGame without carrying local edits.
namespace Microsoft.Xna.Framework.Content
{
    internal class ContentLoadException : Exception
    {
        public ContentLoadException() { }
        public ContentLoadException(string message) : base(message) { }
        public ContentLoadException(string message, Exception inner) : base(message, inner) { }
    }
}
