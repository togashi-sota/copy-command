using UnityEngine;

namespace CopyCommand
{
    /// <summary>【検証中】ゆっくり回る（ワープ陣）。</summary>
    public class Spin : MonoBehaviour
    {
        public float degreesPerSecond = 40f;
        void Update() => transform.Rotate(0f, 0f, degreesPerSecond * Time.deltaTime);
    }
}
