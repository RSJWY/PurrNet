#if YOOASSET_PURRNET_SUPPORT
using NUnit.Framework;
using PurrNet;

public class NetworkYooAssetKeyTests
{
    [Test]
    public void Encode_RoundTripsPackageAndLocation()
    {
        var key = NetworkYooAsset.Encode("DefaultPackage", "Assets/Prefabs/Player.prefab");

        Assert.That(NetworkYooAsset.TryDecode(key, out var packageName, out var location), Is.True);
        Assert.That(packageName, Is.EqualTo("DefaultPackage"));
        Assert.That(location, Is.EqualTo("Assets/Prefabs/Player.prefab"));
    }

    [Test]
    public void Encode_HandlesColonsInLocation()
    {
        var key = NetworkYooAsset.Encode("Pkg", "weird:location:name");

        Assert.That(NetworkYooAsset.TryDecode(key, out var packageName, out var location), Is.True);
        Assert.That(packageName, Is.EqualTo("Pkg"));
        Assert.That(location, Is.EqualTo("weird:location:name"));
    }

    [Test]
    public void TryDecode_RejectsWrongPrefix()
    {
        var prefabKey = YooAssetNetworkPrefabs.Encode("DefaultPackage", "Player");

        Assert.That(NetworkYooAsset.TryDecode(prefabKey, out _, out _), Is.False);
        Assert.That(NetworkYooAsset.TryDecode("purrnet-yooasset-v1:3:PkgScene", out _, out _), Is.False);
    }

    [Test]
    public void TryDecode_RejectsMalformedKeys()
    {
        Assert.That(NetworkYooAsset.TryDecode(null, out _, out _), Is.False);
        Assert.That(NetworkYooAsset.TryDecode(string.Empty, out _, out _), Is.False);
        Assert.That(NetworkYooAsset.TryDecode(NetworkYooAsset.KeyPrefix, out _, out _), Is.False);
        Assert.That(NetworkYooAsset.TryDecode(NetworkYooAsset.KeyPrefix + "3:Pkg", out _, out _), Is.False);
    }

    [Test]
    public void TryDecode_RejectsTruncatedPackageName()
    {
        var key = NetworkYooAsset.KeyPrefix + "10:PkgLoc";

        Assert.That(NetworkYooAsset.TryDecode(key, out _, out _), Is.False);
    }
}
#endif
