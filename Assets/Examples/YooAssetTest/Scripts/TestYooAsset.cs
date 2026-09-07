using PurrNet;
using UnityEngine;

namespace YooAssetTest
{
    /// <summary>Simple network object used inside a YooAsset-delivered scene or prefab.</summary>
    public sealed class TestYooAsset : NetworkIdentity
    {
        protected override void OnSpawned()
        {
            base.OnSpawned();
            if (!isController)
                return;

            transform.position += new Vector3(
                Random.Range(-5f, 5f),
                Random.Range(0f, 2f),
                Random.Range(-5f, 5f));
            TestRpc();
        }

        [ObserversRpc]
        private void TestRpc()
        {
            Debug.Log("YooAsset network object spawned and RPC delivered.");
        }

        [ObserversRpc, PurrButton]
        private void AnotherTestRpc()
        {
            Debug.Log("YooAsset test object RPC invoked.");
        }
    }
}
