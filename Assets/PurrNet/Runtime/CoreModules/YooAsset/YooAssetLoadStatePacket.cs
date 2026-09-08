#if YOOASSET_PURRNET_SUPPORT
using PurrNet.Packing;

namespace PurrNet.Modules
{
    public struct YooAssetLoadStatePacket : IPackedAuto
    {
        public StringUTF8 key;
        public bool loaded;
    }

    public struct YooAssetLoadRequestPacket : IPackedAuto
    {
        public StringUTF8 key;
    }
}
#endif
