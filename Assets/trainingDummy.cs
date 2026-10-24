using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using SamuraiDuel;

public class trainingDummy : MonoBehaviour, ISwordHittable
{
	[SerializeField] private Animator dummyAnimator;

	public CombatTeam Team => CombatTeam.Neutral;

	private void OnCollisionEnter(Collision other)
	{
		if (other.gameObject.CompareTag("Weapon"))
		{
			dummyAnimator.SetTrigger("Death");
		}
	}

	public void ActivateDummy()
	{
		dummyAnimator.SetTrigger("Activate");
	}

	// Swept sword hits (SwordHitDetector) also knock the dummy down, so fast swings that
	// tunnel through the collider between physics steps still register.
	public HitResult ReceiveHit(ref HitInfo hit)
	{
		dummyAnimator.SetTrigger("Death");
		return HitResult.Hit;
	}
}
