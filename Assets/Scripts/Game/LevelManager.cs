using UnityEngine;

namespace SpaceClimb
{
    /// <summary>
    /// Lightweight scene-level reference holder. Other systems pull the
    /// authoritative spawn point and asteroid container from here so we don't
    /// scatter "GameObject.Find" calls across the codebase.
    /// </summary>
    public class LevelManager : MonoBehaviour
    {
        [SerializeField] Transform spawnPoint;
        [SerializeField] Transform asteroidsContainer;

        public Transform SpawnPoint => spawnPoint;
        public Transform AsteroidsContainer => asteroidsContainer;
    }
}
