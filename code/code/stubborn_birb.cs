using Sandbox;
using Sandbox.Effects;
using Sandbox.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace sbox.Community
{
	[Spawnable]
	[Library( "ent_stubborn_birb_npc", Title = "Stubborn Birb" )]
	public partial class Stubborn_Birb : AnimatedEntity
	{
		private AnimatedEntity targetPlayer;
		private Vector3 spawnPoint = Vector3.Zero;
		private birb_Task mission = birb_Task.FlyingToTheTarget;
		private bool lastReach = false;
		private float lastReaching = 0f;
		private float pissingTime = 0f;
		private Vector3 wanderPos = Vector3.Zero;
		private float wanderTime = 0f;
		private int currentWander = 0;
		private int wanderLimitRandom = 0;
		private float health = 100f;
		private bool spawned = false;
		//private static ScreenEffects PP;

		private static Panel pissPanel;

		private readonly List<string> pigeonNoises = new() //hla
		{
			"amb_nature_c17_pigeons_01",
			"amb_nature_c17_pigeons_07",
		};

		enum birb_Task
		{
			WaitToSpawn,
			FlyingToTheTarget,
			DoingJob,
			ReturningTheSpawnPoint
		}

		public Stubborn_Birb() { Spawn(); }

		public override void Spawn()
		{
			if ( IsClient )
				return;

			mission = birb_Task.WaitToSpawn;

			var target = findTarget();
			if ( !target.Item1 )
			{
				Delete();
				Log.Error( "There is no any player!" );
				return;
			}

			targetPlayer = target.Item2;

			var location = findLocationToSpawn();
			if ( !location.Item1 )
			{
				Delete();
				Log.Error( "Not found a suitable spawn point for birb!, Try again.." );
				return;
			}

			spawnPoint = location.Item2;

			mission = birb_Task.FlyingToTheTarget;

			wanderLimitRandom = Rand.Int( 5, 20 );

			base.Spawn();

			Position = spawnPoint;

			Predictable = true;

			var modelName = "models/creatures/pigeon/models/pigeon_simple.vmdl"; //hla

			SetModel( modelName );

			SetupPhysicsFromSphere( PhysicsMotionType.Dynamic, Vector3.Zero, 2f ); //physics
			EnableSelfCollisions = false;
			PhysicsEnabled = false;
			UsePhysicsCollision = false;
			EnableSolidCollisions = false;

			_ = pigeonSounds();
		}

		private (bool, Vector3) findLocationToSpawn( Vector3? pos = null, int radius = 10000 )
		{
			var getPos = pos.GetValueOrDefault( targetPlayer.Position );

			var tryToFind = 100;

			for ( int i = 1; i < tryToFind; i++ )
			{
				var randRadius = Rand.Float( radius / (i / 10).Clamp( 1, tryToFind ) ).Clamp( 100f, radius );

				var rand = Vector3.Random * randRadius;

				var FoundPos = new Vector3( getPos.x + rand.x, getPos.y + rand.y, getPos.z + MathF.Abs(rand.z));

				if ( isReachable( from: FoundPos ) )
					return (true, FoundPos);
			}

			return (false, Vector3.Zero);
		}

		private (bool, AnimatedEntity) findTarget()
		{
			var players = All.Where( x => x.Tags.Has( "player" ) );

			if ( !players.Any() )
				return (false, null);

			var pickedply = players.ToList()[Rand.Int( players.Count() - 1 )];

			return (true, (AnimatedEntity)pickedply);
		}

		private bool isReachable( Vector3? from = null, Vector3? to = null, bool use_cache = false )
		{
			if ( use_cache && lastReaching < Time.Now )
				return !lastReach;

			var tr = Trace.Ray( from ?? Position, to ?? targetPlayer.Position )
			.Ignore( this )
			.Ignore( targetPlayer )
			.Run();

			lastReach = tr.Hit;
			lastReaching = Time.Now + 1f;

			return !lastReach;
		}

		private void follow( bool wander = false, bool gotohome = false )
		{
			if ( wander )
			{
				if ( wanderTime < Time.Now )
				{
					wanderPos = Position + Vector3.Random * 200f;
					wanderTime = Time.Now + 3;
					currentWander++;
				}
			}
			else
				currentWander = 0;

			var substracted = ((wander ? wanderPos : targetPlayer.Position) - Position).EulerAngles;
			substracted.pitch /= 10;
			substracted.yaw *= gotohome ? -1 : 1;
			Rotation = substracted.ToRotation();
			Position = Position.LerpTo( wander ? wanderPos : (gotohome ? spawnPoint : (targetPlayer.Position + (Vector3.Up * targetPlayer.PhysicsBody.GetBounds().Maxs.z))), gotohome ? Time.Delta / 10f : Time.Delta );
		}

		private bool isCloseToTheTarget( bool home = false ) => Position.DistanceSquared( home ? spawnPoint : (targetPlayer.Position + (Vector3.Up * targetPlayer.PhysicsBody.GetBounds().Maxs.z)) ) < (home ? (500 * 500) : (20 * 20)); //GetAngle

		private void doPiss()
		{
			if ( pissingTime < Time.Now )
			{
				pissEffects();
				pissEffects_clside( To.Single( targetPlayer ) );
				mission = birb_Task.ReturningTheSpawnPoint;
				Parent = null;
			}
		}

		public override void TakeDamage( DamageInfo info )
		{
			base.TakeDamage( info );

			health -= info.Damage;
			PlaySound( pigeonNoises[0] ).SetVolume( 5f ).SetPitch( Rand.Float( 1.25f, 1.55f ) );

			Particles.Create( "particles/impact.flesh.vpcf", info.Position );

			if ( health <= 0 )
			{
				Sound.FromWorld( To.Everyone, "amb_nature_c17_pigeons_07_death", info.Position ).SetVolume( 10f );
				Particles.Create( "particles/break/break.cardboard.vpcf", info.Position );

				Delete();
			}
		}

		private void pissEffects()
		{
			//Giving compile error for some reason
			/*var pissPP = new PostProcessingEntity
			{
				PostProcessingFile = "postprocess/birb_poop_effect.vpost"
			};
			pissPP.FadeTime = 0.5f;
			pissPP.Owner = targetPlayer;
			pissPP.Transmit = TransmitType.Owner;
			pissPP.DeleteAsync( 5 );*/

			PlaySound( "birb_poop" ).SetVolume( 1.5f );
			targetPlayer.PlaySound( "ba_ohshit03" ).SetVolume( 1.5f );
		}

		[ClientRpc]
		public static void pissEffects_clside()
		{

			if ( pissPanel == null || !pissPanel.IsValid() )
			{
				pissPanel = Local.Hud.FindRootPanel().Add.Panel();
				pissPanel.Style.Width = Length.Fraction( 1 );
				pissPanel.Style.Height = Length.Fraction( 1 );
			}

			var piss2 = pissPanel.Add.Panel();
			piss2.Style.Position = PositionMode.Absolute;
			piss2.Style.BackgroundImage = Texture.Load( FileSystem.Mounted, "materials/birb_poop/poop2.png" );
			piss2.Style.BackgroundRepeat = BackgroundRepeat.NoRepeat;
			piss2.Style.Opacity = Rand.Float( 0.7f, 0.9f );

			var piss2Size = Rand.Float( 0.6f, 0.9f );
			piss2.Style.Width = Length.Fraction( piss2Size );
			piss2.Style.Height = Length.Fraction( piss2Size );

			piss2.Style.Left = Length.Fraction( Rand.Float( 0f, 0.7f ) );
			piss2.Style.Top = Length.Fraction( Rand.Float( 0f, 0.7f ) );

			var piss1 = pissPanel.Add.Panel();
			piss1.Style.Position = PositionMode.Absolute;
			piss1.Style.BackgroundImage = Texture.Load( FileSystem.Mounted, "materials/birb_poop/poop1.png" );
			piss1.Style.BackgroundRepeat = BackgroundRepeat.NoRepeat;
			piss1.Style.Opacity = Rand.Float( 0.4f, 0.7f );

			var piss1Size = Rand.Float( 0.4f, 0.7f );
			piss1.Style.Width = Length.Fraction( piss1Size );
			piss1.Style.Height = Length.Fraction( piss1Size );

			piss1.Style.Left = Length.Fraction( Rand.Float( 0f, 0.7f ) );
			piss1.Style.Top = Length.Fraction( Rand.Float( 0f, 0.7f ) );

			pissPanel.Style.BackdropFilterBlur = 4f;
			pissPanel.Style.BackdropFilterSaturate = 1.5f;
			pissPanel.Style.BackdropFilterContrast = 0.8f;

			//Giving compile error for some reason
			/*PP = new();
			Map.Camera.AddHook( PP );
			PP.ChromaticAberration.Scale = 2;
			PP.Sharpen = 0.5f;
			PP.Saturation = 1.2f;
			PP.MotionBlur.Scale = 1.5f;
			PP.Enabled = true;*/

			_ = pissEffects_think();
		}

		private static async Task pissEffects_think()
		{
			var cleanEffectsTime = Time.Now + 5;
			while ( cleanEffectsTime > Time.Now && pissPanel != null && pissPanel.IsValid() )
			{
				pissPanel.Style.Opacity = (cleanEffectsTime - Time.Now).Clamp( 0f, 1f );
				await Local.Pawn.Task.Delay( 10 );
			}
			if ( pissPanel.IsValid() )
			{
				pissPanel.DeleteChildren();
				pissPanel.Delete();
			}
			/*PP.Enabled = false;
			Map.Camera.RemoveHook( PP );*/
		}

		private async Task pigeonSounds()
		{
			while ( IsValid )
			{
				PlaySound( pigeonNoises[Rand.Int( pigeonNoises.Count - 1 )] ).SetVolume( Rand.Float( 0.75f, 1.25f ) ).SetPitch( Rand.Float( 0.9f, 1.15f ) );
				await Task.Delay( Rand.Int( 500, 900 ) );
			}
		}

		[Event.Tick.Server]
		protected void Tick()
		{
			//Because npc spawner of sandbox gamemode spawns the birb to the front of you
			if(!spawned && spawnPoint != Vector3.Zero)
			{
				_ = Task.NextPhysicsFrame();
				Position = spawnPoint;
				spawned = true;
			}

			switch ( mission )
			{
				case (birb_Task.FlyingToTheTarget):
					if ( isReachable( use_cache: true ) )
					{
						// targetPlayer alive checking could be possible

						if ( isCloseToTheTarget() )
						{
							Position = targetPlayer.Position + (Vector3.Up * targetPlayer.PhysicsBody.GetBounds().Maxs.z);
							Parent = targetPlayer;

							pissingTime = Time.Now + 5;
							mission = birb_Task.DoingJob;
							PlaySound( "birb_pooping" ).SetVolume( 1.2f );

						}
						else
							follow();
					}
					else
					{
						follow( wander: true );
						if ( currentWander > wanderLimitRandom )
						{
							Delete();
							Log.Error( $"Birb seeked out too much to you and gone... (x{currentWander} times)" );
						}
					}
					break;
				case (birb_Task.DoingJob):
					doPiss();
					break;
				case (birb_Task.ReturningTheSpawnPoint):
					if ( isCloseToTheTarget( home: true ) )
						Delete();
					else
						follow( gotohome: true );
					break;
				default: break;
			}
		}
	}
}
