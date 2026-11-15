using PresetStudio.Model;

namespace PresetStudio.Preview
{
    internal static class ThumbnailCache
    {
        private const int Capacity = 300;

        private static readonly LinkedList<IPreviewItem> order = new();
        private static readonly Dictionary<IPreviewItem, LinkedListNode<IPreviewItem>> nodes = new(ReferenceEqualityComparer.Instance);
        private static readonly Dictionary<object, Func<IPreviewItem, bool>> wantedProviders = new(ReferenceEqualityComparer.Instance);

        public static void Register(object owner, Func<IPreviewItem, bool> isWanted) => wantedProviders[owner] = isWanted;

        public static void Unregister(object owner) => wantedProviders.Remove(owner);

        public static void Touch(IPreviewItem item)
        {
            if (!nodes.TryGetValue(item, out var node))
                return;

            order.Remove(node);
            order.AddLast(node);
        }

        public static void Add(IPreviewItem item)
        {
            if (nodes.TryGetValue(item, out var node))
                order.Remove(node);

            nodes[item] = order.AddLast(item);
            Trim();
        }

        private static void Trim()
        {
            var node = order.First;
            while (nodes.Count > Capacity && node is not null)
            {
                var next = node.Next;
                var item = node.Value;
                if (!wantedProviders.Values.Any(isWanted => isWanted(item)))
                {
                    order.Remove(node);
                    nodes.Remove(item);
                    item.PreviewImage = null;
                }

                node = next;
            }
        }
    }
}
