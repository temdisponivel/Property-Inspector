using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Assets.Plugins.PropertyInspector.Editor
{
    public enum FilterType
    {
        None,
        StartsWith,
        EndsWith,
        Contains,
        Match,
        Type,
        Value,
        Component,
    }

    public static class Constants
    {
        public const string SearchInputControlName = "prop_insp_input_name";
        public const string MultiEditPrefsKey = "prop_insp_multi_edit";
        public const string HideContentPrefsKey = "prop_insp_hide_content";
        public const string UseCustomInspecPrefsKey = "prop_insp_custom_inspec";
        public const string MethodButtonsPrefsKey = "prop_insp_method_buttons";
    }

    public class FilterConfig
    {
        public string FilterText;
        public FilterType FilterType;

        public bool MultiEdit;
        public bool UseCustomInspector;
        public bool ShowMethodButtons;
        public bool HideContent;
    }

    public class DrawableObject
    {
        public List<Component> Components;
        public List<DrawableProperty> Properties;
    }

    public class DrawableComponent
    {
        public Component Component;
        public CustomEditor CustomEditor;
        public List<DrawableProperty> FilteredProperties;
    }

    public class DrawableProperty
    {
        public SerializedProperty SerializedProperty;
    }

    public class WindowState
    {
        public EditorWindow Window;

        public FilterConfig Config;
        public EditorContent Contents;

        public UnityEngine.Object[] LockedSelection;

        public bool LockSelection;
        public bool IsUtilityWindow;

        public float NextSearchTime;

        public List<DrawableObject> CurrentObjects;
        public List<DrawableComponent> CurrentComponents;

        public FrameState LastFrameState;
        public FrameState CurrentFrameState;
    }

    public class FrameState
    {
        public bool ApplyAll;
        public bool ReverAll;

        public bool ExpandAll;
        public bool CollapseAll;

        public bool ShowHelp;
    }

    public class EditorContent
    {
        public GUIContent TitleContent;

        public GUIContent MultiEditToggle;
        public GUIContent UseCustomInspectorToggle;
        public GUIContent ShowMethodButtonsToggle;
        public GUIContent HideContentToggle;

        public GUIContent ApplyAllButtonAvailable;
        public GUIContent ApplyAllButtonUnavailable;
        public GUIContent RevertAllButtonAvailable;
        public GUIContent RevertAllButtonUnavailable;

        public GUIContent ExpandAllButton;
        public GUIContent CollapseAllButton;

        public GUIContent LockButton;
        public GUIContent HelpButton;

        public GUIContent NewPropertyInspectorWindowButton;
    }

    public static class EditorUtil
    {
        public static void DrawWindowHeader(WindowState window)
        {
            GUILayout.BeginVertical("In BigTitle");
            EditorGUILayout.BeginVertical();
            GUILayout.Space(5);
            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.LabelField(window.Contents.TitleContent);

            GUILayout.FlexibleSpace();

            DrawTopHeaderButtons(window);

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginVertical();
            GUILayout.Space(5);
            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginHorizontal();

            DrawTopHeaderInput(window);

            EditorGUILayout.EndHorizontal();

            GUILayout.Space(5);

            EditorGUILayout.BeginHorizontal();

            DrawBottomHeaderToggles(window);

            GUILayout.FlexibleSpace();

            DrawBottomButtons(window);

            EditorGUILayout.EndHorizontal();
            GUILayout.Space(5);
            EditorGUILayout.EndVertical();
            GUILayout.EndVertical();
        }

        public static void DrawTopHeaderInput(WindowState window)
        {
            GUI.SetNextControlName(Constants.SearchInputControlName);

            var search = EditorGUILayout.TextField(window.Config.FilterText, (GUIStyle)"ToolbarSeachTextField", GUILayout.Width(window.Window.position.width - 25));

            if (search != window.Config.FilterText)
            {
                window.NextSearchTime = (float)EditorApplication.timeSinceStartup + .2f;
                window.Config.FilterText = search;
            }

            var style = "ToolbarSeachCancelButtonEmpty";
            if (!string.IsNullOrEmpty(window.Config.FilterText))
                style = "ToolbarSeachCancelButton";

            if (GUILayout.Button(GUIContent.none, style))
            {
                window.Config.FilterText = string.Empty;
                GUIUtility.keyboardControl = 0;
            }
        }

        public static void DrawTopHeaderButtons(WindowState window)
        {
            if (window.IsUtilityWindow)
            {
                var locked = GUILayout.Toggle(window.LockSelection, window.Contents.LockButton, "IN LockButton");
                if (locked != window.LockSelection)
                {
                    window.LockSelection = locked;
                    window.LockedSelection = Selection.objects;
                }

                GUILayout.Space(5);
            }

            if (GUILayout.Button(window.Contents.HelpButton, GUIStyle.none))
            {
                window.CurrentFrameState.ShowHelp = true;
            }
        }

        public static void DrawBottomHeaderToggles(WindowState window)
        {
            window.Config.MultiEdit = EditorGUILayout.ToggleLeft(window.Contents.MultiEditToggle, window.Config.MultiEdit, GUILayout.MaxWidth(60));
            window.Config.UseCustomInspector = EditorGUILayout.ToggleLeft(window.Contents.UseCustomInspectorToggle, window.Config.UseCustomInspector, GUILayout.MaxWidth(105));
            window.Config.ShowMethodButtons = EditorGUILayout.ToggleLeft(window.Contents.ShowMethodButtonsToggle, window.Config.ShowMethodButtons, GUILayout.MaxWidth(105));
            window.Config.HideContent = EditorGUILayout.ToggleLeft(window.Contents.HideContentToggle, window.Config.HideContent, GUILayout.MaxWidth(110));
        }

        public static void DrawBottomButtons(WindowState window)
        {
            var changed = PropertyUtil.HasAnyChange(window);

            GUIContent applyAllContent;
            GUIContent revertAllContent;
            if (changed)
            {
                applyAllContent = window.Contents.ApplyAllButtonAvailable;
                revertAllContent = window.Contents.RevertAllButtonAvailable;
            }
            else
            {
                GUI.enabled = false;
                applyAllContent = window.Contents.ApplyAllButtonUnavailable;
                revertAllContent = window.Contents.RevertAllButtonUnavailable;
            }

            window.CurrentFrameState.ApplyAll = GUILayout.Button(applyAllContent, "miniButtonLeft");
            window.CurrentFrameState.ReverAll = GUILayout.Button(revertAllContent, "miniButtonRight");

            if (!changed)
                GUI.enabled = true;

            window.CurrentFrameState.ExpandAll = GUILayout.Button(window.Contents.ExpandAllButton, "miniButtonLeft");
            window.CurrentFrameState.CollapseAll = GUILayout.Button(window.Contents.CollapseAllButton, "miniButtonRight");
        }

        public static EditorContent CreateDefaultEditorContent()
        {
            var editorContent = new EditorContent();

            editorContent.LockButton = new GUIContent();
            editorContent.LockButton.tooltip = "Lock current selection.";

            editorContent.HelpButton = new GUIContent(EditorGUIUtility.Load("icons/_Help.png") as Texture2D, "Show Help");

            editorContent.MultiEditToggle = new GUIContent("Group", "Group objects of the same type.");
            editorContent.HideContentToggle = new GUIContent("Hide", "Hide all properties (to prevent heavy objects from being draw without a search query).");
            editorContent.UseCustomInspectorToggle = new GUIContent("Custom editor", "Show objects and components using their custom (or default) editors.");
            editorContent.UseCustomInspectorToggle = new GUIContent("Custom editor", "Show objects and components using their custom (or default) editors.");
            editorContent.ShowMethodButtonsToggle = new GUIContent("Methods buttons", "Show available methods to call on objects/components.");

            editorContent.ApplyAllButtonAvailable = new GUIContent("Apply all", "Apply all instance changes to prefabs.");
            editorContent.ApplyAllButtonUnavailable = new GUIContent("Apply all", "There's no changes to apply.");

            editorContent.RevertAllButtonAvailable = new GUIContent("Revert all", "Revert all instance changes to prefabs.");
            editorContent.RevertAllButtonUnavailable = new GUIContent("Revert all", "There's no changes to revert.");

            editorContent.NewPropertyInspectorWindowButton = new GUIContent("New PropertInspector window");

            string textToLoad;
            if (EditorGUIUtility.isProSkin)
                textToLoad = "icons/d_ViewToolZoom.png";
            else
                textToLoad = "icons/ViewToolZoom.png";
            editorContent.TitleContent = new GUIContent("Property Inspector", EditorGUIUtility.Load(textToLoad) as Texture2D);

            if (EditorGUIUtility.isProSkin)
                textToLoad = "icons/d_winbtn_win_max.png";
            else
                textToLoad = "icons/winbtn_win_max.png";
            editorContent.ExpandAllButton = new GUIContent(EditorGUIUtility.Load(textToLoad) as Texture2D, "Expand all.");

            if (EditorGUIUtility.isProSkin)
                textToLoad = "icons/d_winbtn_win_min.png";
            else
                textToLoad = "icons/winbtn_win_min.png";
            editorContent.CollapseAllButton = new GUIContent(EditorGUIUtility.Load(textToLoad) as Texture2D, "Collapse all.");

            return editorContent;
        }

        public static FilterConfig CreateDefaultFilterConfig()
        {
            var config = new FilterConfig();
            config.FilterText = string.Empty;
            config.FilterType = FilterType.None;
            LoadConfigsFromPrefsIntoFilterConfig(config);
            return config;
        }

        public static void LoadConfigsFromPrefsIntoFilterConfig(FilterConfig config)
        {
            config.MultiEdit = EditorPrefs.GetBool(Constants.MultiEditPrefsKey);
            config.HideContent = EditorPrefs.GetBool(Constants.HideContentPrefsKey);
            config.UseCustomInspector = EditorPrefs.GetBool(Constants.UseCustomInspecPrefsKey);
            config.ShowMethodButtons = EditorPrefs.GetBool(Constants.MethodButtonsPrefsKey);
        }

        public static void SaveConfigsFromFilterConfigIntoPrefs(FilterConfig config)
        {
            EditorPrefs.SetBool(Constants.MultiEditPrefsKey, config.MultiEdit);
            EditorPrefs.SetBool(Constants.HideContentPrefsKey, config.HideContent);
            EditorPrefs.SetBool(Constants.UseCustomInspecPrefsKey, config.UseCustomInspector);
            EditorPrefs.SetBool(Constants.MethodButtonsPrefsKey, config.ShowMethodButtons);
        }

        public static void OpenNewWindow(bool utility)
        {
            var windowState = new WindowState();

            windowState.Contents = CreateDefaultEditorContent();
            windowState.Config = CreateDefaultFilterConfig();
            windowState.IsUtilityWindow = utility;
            windowState.LockSelection = false;
            windowState.NextSearchTime = -1;
            windowState.CurrentComponents = new List<DrawableComponent>();
            windowState.CurrentObjects = new List<DrawableObject>();
            windowState.CurrentFrameState = new FrameState();
            windowState.LastFrameState = new FrameState();

            var window = UnityEditor.Editor.CreateInstance<PropertyInspectorWindow>();
            window.WindowState = windowState;
            windowState.Window = window;

            if (utility)
                window.ShowUtility();
            else
                window.Show();
        }

        [MenuItem("Window/PropertyInspector window &s")]
        public static void OpenUtilityMenu()
        {
            OpenNewWindow(true);
        }

        [MenuItem("Window/PropertyInspector tab")]
        public static void OpenWindowMenu()
        {
            OpenNewWindow(false);
        }
    }
    
    public static class PropertyUtil
    {
        public static bool HasAnyChange(WindowState window)
        {
            //for (int i = 0; i < _drawable.Count; i++)
            //{
            //    if (_drawable[i].HasAppliableChanges)
            //    {
            //        changed = true;
            //        break;
            //    }
            //}
            return true;
        }
    }

    public class PropertyInspectorWindow : EditorWindow, IHasCustomMenu
    {
        public WindowState WindowState;

        void OnGUI()
        {
            EditorUtil.DrawWindowHeader(WindowState);
        }

        public void AddItemsToMenu(GenericMenu menu)
        {
            var windowState = WindowState;
            menu.AddItem(WindowState.Contents.LockButton, windowState.LockSelection, () =>
            {
                windowState.LockSelection = !windowState.LockSelection;
                windowState.LockedSelection = Selection.objects;
            });
            menu.AddItem(windowState.Contents.NewPropertyInspectorWindowButton, false, () => EditorUtil.OpenNewWindow(false));
        }
    }
}