using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

public class GameBootstrapTests
{
    private const string BaseScenePath = "Assets/Scenes/Tests/Base/BaseTest.unity";

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        DestroyEntries();
        yield return null;
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        DestroyEntries();
        yield return null;
        Scene scene = SceneManager.GetSceneByPath(BaseScenePath);
        if (scene.IsValid() && scene.isLoaded)
        {
            yield return SceneManager.UnloadSceneAsync(scene);
        }
    }

    [UnityTest]
    public IEnumerator DuplicateEntryKeepsOriginalInstance()
    {
        GameBootstrap original = new GameObject("Original Entry").AddComponent<GameBootstrap>();
        GameBootstrap duplicate = new GameObject("Duplicate Entry").AddComponent<GameBootstrap>();

        Assert.That(duplicate.enabled, Is.False);
        yield return null;

        Assert.That(duplicate == null, Is.True);
        Assert.That(GameBootstrap.Instance, Is.SameAs(original));
        Assert.That(FindEntries().Length, Is.EqualTo(1));
    }

    [UnityTest]
    public IEnumerator DestroyedEntryClearsReferenceAndCanBeRecreated()
    {
        GameBootstrap original = new GameObject("Original Entry").AddComponent<GameBootstrap>();
        Object.Destroy(original.gameObject);
        yield return null;

        Assert.That(GameBootstrap.Instance, Is.Null);
        GameBootstrap replacement = new GameObject("Replacement Entry").AddComponent<GameBootstrap>();
        Assert.That(GameBootstrap.Instance, Is.SameAs(replacement));
        Assert.That(FindEntries().Length, Is.EqualTo(1));
    }

    [UnityTest]
    public IEnumerator BaseSceneCanReloadWithoutReplacingPersistentEntry()
    {
#if UNITY_EDITOR
        GameBootstrap original = null;
        for (int entry = 0; entry < 2; entry++)
        {
            // 直接加载真实测试场景，不为测试修改正式Build Settings。
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(BaseScenePath,
                new LoadSceneParameters(LoadSceneMode.Additive));
            yield return null;

            Scene scene = SceneManager.GetSceneByPath(BaseScenePath);
            Assert.That(scene.IsValid() && scene.isLoaded, Is.True);
            if (entry == 0)
            {
                original = GameBootstrap.Instance;
            }
            Assert.That(original, Is.Not.Null);
            Assert.That(GameBootstrap.Instance, Is.SameAs(original));
            Assert.That(FindEntries().Length, Is.EqualTo(1));
            Assert.That(original.gameObject.scene.name, Is.EqualTo("DontDestroyOnLoad"));

            yield return SceneManager.UnloadSceneAsync(scene);
            Assert.That(original != null, Is.True);
            Assert.That(GameBootstrap.Instance, Is.SameAs(original));
        }
#else
        Assert.Ignore("此场景测试使用Editor加载路径，不修改正式Build Settings。");
        yield break;
#endif
    }

    private static GameBootstrap[] FindEntries()
    {
        return Object.FindObjectsByType<GameBootstrap>(FindObjectsInactive.Include, FindObjectsSortMode.None);
    }

    private static void DestroyEntries()
    {
        foreach (GameBootstrap entry in FindEntries())
        {
            Object.Destroy(entry.gameObject);
        }
    }
}
