using Sandbox;
using Sandbox.Diagnostics;
using Sandbox.UI;
using System;
using System.Linq;
using System.Threading.Tasks;

public sealed class StubbornBirb : Component, Component.IDamageable, Component.INetworkSpawn
{
	private GameObject target;
	private SkinnedModelRenderer birdRenderer;
	private (ModelPhysics modelphysics,Rigidbody rigidbody) targetModelPhysics = new();
	private Vector3 targetOffset;
	private Vector3 spawnPoint = Vector3.Zero;
	private birb_Task task = birb_Task.FlyingToTheTarget;
	private birb_Mission mission;
	private bool lastReach = false;
	private float lastReaching = 0f;
	private float pissingTime = 0f;
	private Vector3 wanderPos = Vector3.Zero;
	private float wanderTime = 0f;
	private int currentWander = 0;
	private int wanderLimitRandom = Game.Random.Int( 5, 20 );
	private float health = 10f;
	private bool spawned = false;
	private Vector3 prevPos = Vector3.Zero;
	private Logger logger = new Logger( "StubbornBirb" );
	private SoundHandle noise;
	private SoundHandle pooping;
	private float orbitPhase = Game.Random.Float( 0f, MathF.PI* 2f );
    private float hoverPhase = Game.Random.Float( 0f, MathF.PI* 2f );
    private float orbitRadius = Game.Random.Float( 15f, 30f );
	//private bool settled = false;

	//Files
	[Property, Group("Files")] SoundEvent birb_Damage { get; set; }
	[Property, Group("Files")] SoundEvent birb_Noise { get; set; }
	[Property, Group("Files")] SoundEvent birb_Poop { get; set; }
	[Property, Group("Files")] SoundEvent birb_Pooping { get; set; }
	[Property, Group("Files")] Texture birb_Poop1 { get; set; }
	[Property, Group("Files")] Texture birb_Poop2 { get; set; }

	enum birb_Task
	{
		WaitToSpawn,
		FlyingToTheTarget,
		DoingJob,
		ReturningTheSpawnPoint
	}

	enum birb_Mission
	{
		DealWithPlayer,
		StealProp,

	}
	public enum bird_NetworkMessages
	{
		Popping,
		Error
	}

	protected override void OnStart()
	{
		Tags.Add( "stubborn_birb" );
		WorldPosition = WorldPosition + new Vector3( 0, 0, -10000 ); // Start far below the map until initialized
		birdRenderer = GetComponent<SkinnedModelRenderer>();

		base.OnStart();
	}

	//protected override void OnStart() // Use a initialization method instead because OnStart is early called before network ownership is assigned, so owner can't be determined
	private void Init()
	{
		//base.OnStart();

		if ( Networking.IsClient ) // Game.IsClient?
			return;

		if ( Scene.GetAllObjects( true ).Count( x => x.Tags.Has( "stubborn_birb" ) && x.Network.OwnerId == Network.OwnerId ) >= 2 )
		{
			InfoClient( bird_NetworkMessages.Error, $"You own too many birbs!, max 2" );
			DestroyGameObject();
			return;
		}

		bool tryAgain = false;

		task = birb_Task.WaitToSpawn;
		mission = DetermineMission();

		tryAgain:
		var IsTargetPlayer = mission == birb_Mission.DealWithPlayer;
		var _target = findTarget( IsTargetPlayer );
		if ( !_target.targetFound )
		{
			if( !tryAgain ) // If there is no target found, try to find another type of target
			{
				if ( IsTargetPlayer )
					mission = birb_Mission.StealProp;
				else
					mission = birb_Mission.DealWithPlayer;

				tryAgain = true;
				goto tryAgain;
			}

			DestroyGameObject();

			InfoClient( bird_NetworkMessages.Error, $"There is no any {(IsTargetPlayer ? $"player" : "prop")}!");

			return;
		}
		
		target = _target.foundTargetGO;

		var localBounds = target.GetLocalBounds();
		var bounds = localBounds.Size.IsNaN ? BBox.FromPositionAndSize( Vector3.Zero, 100f ) : localBounds;

		if ( IsTargetPlayer )
		{
			targetOffset = Vector3.Up * bounds.Size.z;
		}
		else
		{
			Vector3 topCenterLocal = new Vector3(
				bounds.Center.x,
				bounds.Center.y,
				bounds.Maxs.z
			);

			targetOffset = topCenterLocal;
		}

		var findOutSpawnLocation = FindLocationToSpawn( target.WorldPosition + targetOffset );
		if ( !findOutSpawnLocation.isFound )
		{
			DestroyGameObject();
			InfoClient( bird_NetworkMessages.Error, "A suitable spawn point for birb is not found! Try again.." );
			return;
		}

		spawnPoint = findOutSpawnLocation.foundPos;

		task = birb_Task.FlyingToTheTarget;

		WorldPosition = spawnPoint;

		//Predictable = true;

		_ = PigeonSounds();
	}

	private birb_Mission DetermineMission() => Game.Random.Int( 0, 1 ) == 1 ? birb_Mission.DealWithPlayer : birb_Mission.StealProp;
	private (bool isFound, Vector3 foundPos) FindLocationToSpawn( Vector3 targetPos, int radius = 10000 )
	{
		var tryToFind = 1000;

		for ( int i = 0; i < tryToFind; i++ )
		{
			var randRadius = Game.Random.Float( radius / (i / 10).Clamp( 1, tryToFind ) ).Clamp( 2000f, radius );

			var rand = Vector3.Random * randRadius;

			var foundPos = new Vector3( targetPos.x + rand.x, targetPos.y + rand.y, targetPos.z + MathF.Abs( rand.z ) );
			
			if ( IsReachable( from: foundPos, to: targetPos ) )
				return (true, foundPos);
		}

		return (isFound: false, foundPos: Vector3.Zero);
	}
	 
	private (bool targetFound, GameObject foundTargetGO) findTarget(bool IsPlayer)
	{
		var targets = Scene.GetAllObjects(false).Where( x => x.IsRoot && x.Tags.Has( IsPlayer ? "player" : "removable" ) && x.Name != "stubborn_birb" );

		if ( !targets.Any() )
			return (false, null);

		var picked = targets.ToList()[Game.Random.Int( targets.Count() - 1 )]; // from targets or props gameobjects

		return (targetFound: true, foundTargetGO: picked);
	}

	private bool IsReachable( Vector3 from, Vector3 to, bool use_cache = false )
	{
		if ( use_cache && lastReaching > Time.Now )
			return !lastReach;

		var tr = Scene.Trace.Ray( from, to )
		.IgnoreGameObject(GameObject)
		.WithAnyTags( "solid", "world" )
		.WithoutTags("removable", "player")
		.Run();

		lastReach = tr.Hit;
		
		if ( lastReach )
			return false;

		lastReaching = Time.Now + 0.2f;

		return true;
	}

	private void Move(Vector3 position)
	{
		if( currentWander != 0)
			currentWander = 0;

		// Animation handling
		{
			float speed = (WorldPosition - prevPos).Length / Time.Delta;
		
			if(speed > 600f) // TODO: Check also z-axis movement
			{
				if(!birdRenderer.GetBool("gliding") || birdRenderer.GetBool( "flapping" ) )
				{
					birdRenderer.Set( "flapping", false );
					birdRenderer.Set( "gliding", true );
				}
			}
			else
			{
				if ( !birdRenderer.GetBool( "flapping" ) || birdRenderer.GetBool( "gliding" ) )
				{
					birdRenderer.Set( "flapping", true );
					birdRenderer.Set( "gliding", false );
				}
			}

			prevPos = WorldPosition;
		}

		var angles = (position - WorldPosition).EulerAngles;
		angles.pitch /= 10;

		WorldRotation = angles.ToRotation();
		WorldPosition = position;

	}

	private void Wander()
	{
		if ( wanderTime < Time.Now )
		{
			wanderPos = (new Vector3( 1, 1, 0 ) * (WorldPosition + Vector3.Random * 200f)) + (Vector3.Up * (WorldPosition + Vector3.Random * 10f)); //Z-axis should be less
			wanderTime = Time.Now + 3;
			currentWander++;
		}

		var pos = WorldPosition.LerpTo( wanderPos , Time.Delta * 2f );
		Move( pos );
	}

	private void GotoHome(float speed = 0.25f)
	{
		var pos = WorldPosition.LerpTo(spawnPoint, Time.Delta * speed );
		Move( pos );
	}

	private void MoveToTheTarget(float speed)
	{
		float t = Time.Now;

		Vector3 orbit = target.WorldTransform.Right * MathF.Cos( t * 1.5f + orbitPhase ) + target.WorldTransform.Forward * MathF.Sin( t * 1.5f + orbitPhase );
		orbit *= orbitRadius;

		Vector3 hover = Vector3.Up * MathF.Sin( t * 3f + hoverPhase ) * 8f;
		Vector3 targetPos = target.WorldPosition + targetOffset + orbit + hover;

		var pos = WorldPosition.LerpTo( targetPos, Time.Delta * speed );

		Move( pos );
	}

	private bool IsCloseToTheTarget( Vector3 pos, int tolerance = 20 ) => WorldPosition.DistanceSquared( pos ) < tolerance * tolerance;

	private void DoPiss()
	{
		if ( pissingTime < Time.Now )
		{
			PissEffects();

			using ( Rpc.FilterInclude( c => c == target.Network.Owner ) )
			{
				InfoClient( bird_NetworkMessages.Popping );
			}

			task = birb_Task.ReturningTheSpawnPoint;
			//settled = false;
			//GetComponent<BoxCollider>().Enabled = true;
			//Parent = null;
		}

		MoveToTheTarget( speed: 40f );
	}

	private void PissEffects()
	{
		/*var pissPP = new PostProcessingEntity
		{
			PostProcessingFile = "postprocess/birb_poop_effect.vpost"
		};
		pissPP.FadeTime = 0.5f;
		pissPP.Owner = target;
		pissPP.Transmit = TransmitType.Owner;
		pissPP.DeleteAsync( 5 );*/

		Sound.Play( "birb_poop", target.WorldPosition );
	}

	[Rpc.Broadcast]
	public void InfoClient( bird_NetworkMessages flag, string error = "" )
	{
		switch(flag)
		{
			case bird_NetworkMessages.Popping:
			{
				var sp = Game.ActiveScene.GetAllComponents<ScreenPanel>().First();

				if ( sp == null )
				{
					// We need to create a ScreenPanel to draw on the screen PissPanel
					sp = Game.ActiveScene.AddComponent<ScreenPanel>();
					var pp = sp.AddComponent<PissPanel>();
					pp.sp = sp;
				}
				else
					sp.AddComponent<PissPanel>();
				break;
			}
			case bird_NetworkMessages.Error:
			{
				logger.Error( error );
				break;
			}
			default:
				break;
		}
	}

	private async Task PigeonSounds()
	{
		/*var birdnoise = ResourceLibrary.Get<SoundEvent>( "sounds/pigeon/birb_noise.sound" );*/
		//noise = GameObject.AddComponent<SoundPointComponent>();
		//noise.SoundEvent = birdnoise;
		//noise.Repeat = true;
		//noise.MinRepeatTime = 2f;
		//noise.MaxRepeatTime = 6f;
		//noise.StartSound();

		while ( IsValid && GameObject.IsValid )
		{
			noise = Sound.Play( "birb_noise" );
			if ( noise != null )
			{
				noise.FollowParent = true;
				noise.Parent = GameObject;
			}

			await Task.Delay( Game.Random.Int( 3000, 5000 ) );
		}
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();

		//Because npc spawner of sandbox gamemode spawns the birb to the front of you
		if ( !spawned && spawnPoint != Vector3.Zero )
		{
			_ = Task.FixedUpdate(); //NextPhysicsFrame
			WorldPosition = spawnPoint;
			spawned = true;
		}

		switch ( task )
		{
			case (birb_Task.FlyingToTheTarget):
				if ( IsReachable( from:WorldPosition, to:target.WorldPosition + targetOffset, use_cache: true ) )
				{
					// Target alive checking could be possible

					if ( IsCloseToTheTarget( target.WorldPosition + targetOffset, mission == birb_Mission.DealWithPlayer ? 25 : 30 ) )
					{
						//Position = target.Position + (Vector3.Up * target.PhysicsBody.GetBounds().Size.z);
						//Parent = target;

						task = birb_Task.DoingJob;

						if(mission == birb_Mission.DealWithPlayer )
						{
							//settled = true;
							pissingTime = Time.Now + 5;
							pooping = Sound.Play( "birb_pooping", WorldPosition );
							//GetComponent<BoxCollider>().Enabled = false; // When settled, disable collider
						}
						else if(mission == birb_Mission.StealProp)
						{
							// We are disabling ModelPhysics and/or Rigidbody of the target prop/entity 
							targetModelPhysics.modelphysics = target.GetComponent<ModelPhysics>(); //TODO: GetComponentInChildren

							if ( targetModelPhysics.modelphysics != null )
								targetModelPhysics.modelphysics.Enabled = false;

							targetModelPhysics.rigidbody = target.GetComponent<Rigidbody>();

							if ( targetModelPhysics.rigidbody != null )
								targetModelPhysics.rigidbody.Enabled = false;

							if ( !target.GetLocalBounds().Size.IsNaN)
								targetOffset = target.GetLocalBounds().ClosestPoint( target.WorldTransform.PointToLocal(WorldPosition) ); // Closest point to the GameObject

							WorldPosition = target.WorldPosition + targetOffset;

							try
							{
								if ( target != null && this != null && GameObject != null )
									target.SetParent( GameObject ); // Cant parent to different scene?
							}
							catch { }

							task = birb_Task.ReturningTheSpawnPoint;
	
						}

					}
					else
						MoveToTheTarget(speed: mission == birb_Mission.DealWithPlayer ? 2f : 1.5f );
				}
				else
				{
					Wander();
					if ( currentWander > wanderLimitRandom )
					{
						task = birb_Task.ReturningTheSpawnPoint;
						InfoClient(bird_NetworkMessages.Error, $"Birb seeked out too much and gone... (x{currentWander} times)" );
					}
				}
				break;
			case (birb_Task.DoingJob):
				{
					if ( mission == birb_Mission.DealWithPlayer )
						DoPiss();
				}
				break;
			case (birb_Task.ReturningTheSpawnPoint):
				if ( IsCloseToTheTarget( spawnPoint, 500 ) )
					DestroyGameObject();
				else
					GotoHome( speed: mission == birb_Mission.DealWithPlayer ? 0.25f : 0.095f );
				break;
			default: break;
		}
	}

	void IDamageable.OnDamage( in DamageInfo info )
	{
		health -= info.Damage;
		Sound.Play( "birb_damage", WorldPosition );

		var go = GameObject.Clone( "/prefabs/surface/flesh_bullet.prefab" );
		go.WorldPosition  = info.Position;
		if ( health <= 0 )
		{
			var go2 = GameObject.Clone( "/prefabs/surface/cardboard-bullet.prefab" );
			go2.WorldPosition = info.Position;

			if (mission == birb_Mission.StealProp && target != null ) // Drop the prop if birb is carrying one
			{

				if ( targetModelPhysics.modelphysics != null )
					targetModelPhysics.modelphysics.Enabled = true;

				if ( targetModelPhysics.rigidbody != null )
					targetModelPhysics.rigidbody.Enabled = true;

				try
				{
					target.SetParent( null );// Object should not be null?
				}
				catch { }

			}
			else
			{
				if ( pooping != null )
					pooping.Stop();
			}

			if ( noise != null )
				noise.Stop();

			// TODO: need to be destroyed
			//go.Destroy();
			//go2.Destroy();

			DestroyGameObject();
		}
		//else 
			//go.Destroy();

	}
	public void OnNetworkSpawn( Connection owner )
	{
		Network.AssignOwnership( Rpc.Caller );
		Init();
	}
}

public class PissPanel : PanelComponent
{
	private float cleanEffectsTime;
	public ScreenPanel sp;

	protected override async void OnStart()
	{
		cleanEffectsTime = Time.Now + 5;

		await Task.Delay( 10 );

		if ( this is not null && this.IsValid() )
		{
			Panel.Style.Width = Length.Fraction( 1 );
			Panel.Style.Height = Length.Fraction( 1 );
			Panel.Style.Position = PositionMode.Absolute;
			var piss2 = Panel.Add.Panel();
			piss2.Style.Position = PositionMode.Absolute;
			piss2.Style.BackgroundImage = Texture.LoadFromFileSystem( "materials/birb_poop/poop2.vtex", FileSystem.Mounted );
			piss2.Style.BackgroundRepeat = BackgroundRepeat.NoRepeat;
			piss2.Style.Opacity = Game.Random.Float( 0.7f, 0.9f );
			piss2.Style.Order = Game.Random.Int( 999999 );

			var piss2Size = Game.Random.Float( 0.6f, 0.9f );
			piss2.Style.Width = Length.Fraction( piss2Size );
			piss2.Style.Height = Length.Fraction( piss2Size );

			piss2.Style.Left = Length.Fraction( Game.Random.Float( 0f, 0.7f ) );
			piss2.Style.Top = Length.Fraction( Game.Random.Float( 0f, 0.7f ) );

			var piss1 = Panel.Add.Panel();
			piss1.Style.Position = PositionMode.Absolute;
			piss1.Style.BackgroundImage = Texture.LoadFromFileSystem( "materials/birb_poop/poop1.vtex", FileSystem.Mounted );
			piss1.Style.BackgroundRepeat = BackgroundRepeat.NoRepeat;
			piss1.Style.Opacity = Game.Random.Float( 0.4f, 0.7f );
			piss1.Style.Order = Game.Random.Int( 999999 );

			var piss1Size = Game.Random.Float( 0.4f, 0.7f );
			piss1.Style.Width = Length.Fraction( piss1Size );
			piss1.Style.Height = Length.Fraction( piss1Size );

			piss1.Style.Left = Length.Fraction( Game.Random.Float( 0f, 0.7f ) );
			piss1.Style.Top = Length.Fraction( Game.Random.Float( 0f, 0.7f ) );

			Panel.Style.BackdropFilterBlur = 4f;
			Panel.Style.BackdropFilterSaturate = 1.5f;
			Panel.Style.BackdropFilterContrast = 0.8f;

			//Giving compile error for some reason
			/*PP = new();
			Map.Camera.AddHook( PP );
			PP.ChromaticAberration.Scale = 2;
			PP.Sharpen = 0.5f;
			PP.Saturation = 1.2f;
			PP.MotionBlur.Scale = 1.5f;
			PP.Enabled = true;*/

		}
		base.OnStart();
	}

	protected override async void OnUpdate()
	{
		base.OnUpdate();

		while ( cleanEffectsTime > Time.Now && this != null && this.IsValid() )
		{
			Panel.Style.Opacity = (cleanEffectsTime - Time.Now).Clamp( 0f, 1f );
			await GameTask.Delay( 10 );
		}

		if ( cleanEffectsTime < Time.Now && this.IsValid() )
		{
			if( sp != null ) // Deleting the ScreenPanel after effect ends which is created for temporary
			{
				sp.Destroy();
				sp = null;
			}
			Destroy();
		}
		/*PP.Enabled = false;
		Map.Camera.RemoveHook( PP );*/
	}
}
