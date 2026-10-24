using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class dummyTrigger : MonoBehaviour
{
    [SerializeField] private GameObject[] TargetDummies;

    // Optional hook so other systems (e.g. the samurai duel start plate) can reuse this pressure plate.
    [SerializeField] private UnityEvent onPlayerEnter = new UnityEvent();

    public UnityEvent OnPlayerEnter => onPlayerEnter;

    private void OnTriggerEnter(Collider other)
    {
        if(other.gameObject.CompareTag("Player"))
        {
            if (TargetDummies != null)
            {
                foreach(GameObject dummies in TargetDummies)
                {
                    if (dummies == null || !dummies.activeInHierarchy)
                        continue;
                    trainingDummy dummy = dummies.GetComponent<trainingDummy>();
                    if (dummy != null)
                        dummy.ActivateDummy();
                }
            }

            onPlayerEnter.Invoke();
        }
    }
}
