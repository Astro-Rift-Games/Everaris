using System.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Sweeps a player-sized movement body through every access of CatacombsMap against WorldCollision:
/// closed doors block at their blocker, open doors, the open arch and every stair let the body through.
/// </summary>
public sealed class CatacombsAccessTraversalTests
{
    private const string ScenePath = "Assets/Scenes/CatacombsMap.unity";
    private const string PlayerPath = "Assets/Prefabs/NetworkPlayer.prefab";

    private Scene _scene;
    private PhysicsScene2D _physics;
    private int _mask;
    private Vector2 _bodySize;
    private Vector2 _bodyOffset;

    [OneTimeSetUp]
    public void OpenScene()
    {
        var player = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath);
        var body = player.GetComponents<BoxCollider2D>().Single(c => !c.isTrigger);
        _bodySize = body.size;
        _bodyOffset = body.offset;
        _mask = LayerMask.GetMask("WorldCollision");
        _scene = EditorSceneManager.OpenPreviewScene(ScenePath);
        _physics = _scene.GetPhysicsScene2D();
    }

    [OneTimeTearDown]
    public void CloseScene() => EditorSceneManager.ClosePreviewScene(_scene);

    [Test]
    public void EveryDoor_BlocksWhenClosed_AndLetsAPlayerThroughWhenOpen()
    {
        NetworkDoor[] doors = _scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<NetworkDoor>(true)).ToArray();
        Assert.That(doors, Has.Length.EqualTo(20));
        foreach (NetworkDoor door in doors)
        {
            Vector2 south = (Vector2)door.transform.position + new Vector2(0f, -1.5f);
            var blocker = door.transform.Find("Blocker").GetComponent<BoxCollider2D>();

            RaycastHit2D closed = Sweep(south, 6f);
            Assert.That(closed.collider, Is.SameAs(blocker), $"closed door at {door.transform.position}");

            // From the north a closed door stops the body at the wall's back face, never inside the doorway.
            // Start just north of the wall's back face (some doorways have another wall two rows further north).
            Vector2 north = (Vector2)door.transform.position + new Vector2(0f, 4.5f);
            RaycastHit2D fromNorth = _physics.BoxCast(north + _bodyOffset, _bodySize, 0f, Vector2.down, 5f, _mask);
            Assert.That(fromNorth.collider, Is.SameAs(blocker), $"closed door at {door.transform.position} from the north");
            Assert.That(fromNorth.point.y, Is.EqualTo(door.transform.position.y + 3f).Within(0.01f),
                "The body stops behind the 3 door rows, out of weapon reach of the threshold.");

            blocker.enabled = false;
            try
            {
                RaycastHit2D open = Sweep(south, 6f);
                Assert.That(open.collider, Is.Null,
                    $"open door at {door.transform.position} blocked by {(open.collider ? open.collider.name : "")} after {open.distance:0.00}");
            }
            finally
            {
                blocker.enabled = true;
            }
        }
    }

    // x = centre of the walkway, from = a point south of the access, length = through the access.
    [TestCase("open arch", -79.5f, -69.5f, 5.5f)]
    [TestCase("open arch -28,-65", -26.5f, -66.5f, 5.5f)]
    [TestCase("stair -56,-68", -54f, -69.5f, 6.5f)]
    [TestCase("stair 47,-44", 48.5f, -45.5f, 6.5f)]
    [TestCase("stair 17,-37", 19f, -38.5f, 10.5f)]
    [TestCase("stair -25,24", -23f, 22.5f, 7.5f)]
    [TestCase("stair under arch -74,-49", -72.5f, -50.5f, 8.5f)]
    [TestCase("stair in wall gap 72,-52", 73.5f, -53.5f, 6.5f)]
    public void Access_LetsAPlayerWalkThroughItsWholeLength(string access, float x, float fromY, float length)
    {
        RaycastHit2D hit = Sweep(new Vector2(x, fromY), length);
        Assert.That(hit.collider, Is.Null, $"{access} blocked by {(hit.collider ? hit.collider.name : "")} after {hit.distance:0.00}");
    }

    private RaycastHit2D Sweep(Vector2 root, float distance) =>
        _physics.BoxCast(root + _bodyOffset, _bodySize, 0f, Vector2.up, distance, _mask);
}
