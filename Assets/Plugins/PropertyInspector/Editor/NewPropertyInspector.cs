#define DEB

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Assets.Plugins.PropertyInspector.Editor
{
    /// <summary>
    /// Enumerates all possible filter types.
    /// </summary>
    public enum FilterType
    {
        None,
        Contains,
        StartsWith,
        EndsWith,
        Match,
        Type,
        Value,
        Component,
    }

    /// <summary>
    /// Holds all constants.
    /// </summary>
    public static class Constants
    {
        public const string ObjectHeaderPrefixFormat = "prop_insp_obj_{0}";

        public const string SearchInputControlName = "prop_insp_input_name";
        public const string MultiEditPrefsKey = "prop_insp_multi_edit";
        public const string HideContentPrefsKey = "prop_insp_hide_content";
        public const string UseCustomInspecPrefsKey = "prop_insp_custom_inspec";
        public const string MethodButtonsPrefsKey = "prop_insp_method_buttons";

        public const string StartsWithPrefix = "s:";
        public const string EndsWithPrefix = "e:";
        public const string MatchPrefix = "m:";
        public const string TypePrefix = "t:";
        public const string ValuePrefix = "v:";
        public const string ComponentPrefix = "c:";

        public const string Version = "v1.0.0.6";
        public const string HelpTitle = "About PropertyInspector " + Version;
        public const string HelpMessage = @"Use the search bar to filter a property.

------------------------------------------------------------------------------------------------------------------------
You can use the prefixed: “s:”, “e:”, “t:”, “m:”, “v:”.
“s:”: Starts with - will show only properties whose names starts with the text typed.
“e:”: Ends with - will show only properties whose names ends with the text typed.
“t:”: Type - will show only properties whose type match the text typed.
“v:”: Value - will show properties whose values constains the text typed - this will work better for strings, 
integers, floats and booleans; Unity types like vectors and color are supported as well, but you have to type the value
that would appear if ToString were called on the property.
“m:”: Match - will show properties whose name exactly match the typed text.

None of those options are case sensitive.

------------------------------------------------------------------------------------------------------------------------

You can search using the path of the property you want to see.
For example: 'Player.HealthHandler.Life' would only show the property Life of HealthHandler of Player.  
These options ARE case sensitive.

------------------------------------------------------------------------------------------------------------------------
Multi-edit group objects and components by type and lets you edit multiple objects as if they were one. 
All changes made on this mode affect all object in the group.

------------------------------------------------------------------------------------------------------------------------
Inspector mode will show all properties of all object when there’s no search typed.

------------------------------------------------------------------------------------------------------------------------
Apply all/Revert all will apply or revert all changes made in objects that are instances of prefabs.

------------------------------------------------------------------------------------------------------------------------
Apply/Revert buttons in headers will apply or revert changes made in that object.

------------------------------------------------------------------------------------------------------------------------
The Highlight button highlights the objects in the hierarchy or project or, if on multi-edit, select the clicked group.

------------------------------------------------------------------------------------------------------------------------
All changes made with Property Inspector can be undone (CTRL + Z | CMD + Z) - except apply/revert.

------------------------------------------------------------------------------------------------------------------------
If you have any question, ran into bug or problem or have a suggestion
please don’t hesitate in contating me at: temdisponivel@gmail.com.
For more info, please see the pdf file inside PropertyInspector’s folder or visit: http://goo.gl/kyX3A3";

    }

    /// <summary>
    /// Holds the current filter configuration.
    /// </summary>
    public class FilterConfig
    {
        public string FilterText;
        public string FilterTextLower;
        public string FullFilterText;
        public FilterType FilterType;

        public bool MultiEdit;
        public bool UseCustomInspector;
        public bool ShowMethodButtons;
        public bool HideContent;
    }

    /// <summary>
    /// Defines a object (something that can have components and properties)
    /// to be draw.
    /// </summary>
    public class DrawableObject
    {
        public string ObjectIdentifier;
        public Type ObjectType;
        public UnityEditor.Editor Editor;

        public SerializedObject SerializedObject;
        public List<UnityEngine.Object> Objects;
        public List<SerializedProperty> FilteredProperties;
    }

    /// <summary>
    /// Holds the state of a property inspector window.
    /// </summary>
    public class WindowState
    {
        public EditorWindow Window;

        public FilterConfig Config;
        public EditorContent Contents;

        public UnityEngine.Object[] Selection;

        public bool LockSelection;

        public float NextSearchTime;

        public List<DrawableObject> CurrentObjects;

        public FrameState LastFrameState;
        public FrameState CurrentFrameState;
    }

    /// <summary>
    /// Holds a state of a frame.
    /// This will be flushed every frame.
    /// </summary>
    public class FrameState
    {
        public bool ApplyAll;
        public bool ReverAll;

        public bool ExpandAll;
        public bool CollapseAll;

        public bool ShowHelp;

        public bool ChangedToggles;

        public Vector2 ScrollPosition;
    }

    /// <summary>
    /// Caches all GUIContent to prevent creating it every OnGUI call.
    /// </summary>
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

    /// <summary>
    /// The actuall window of the plugin.
    /// </summary>
    public class PropertyInspectorWindow : EditorWindow
    {
        public WindowState WindowState;

        void OnGUI()
        {
            // this can happen when a build is performed
            if (WindowState == null)
            {
                Close();
                return;
            }

            WindowState.CurrentFrameState = new FrameState();

            EditorUtil.DrawWindowHeader(WindowState);

            PropertyUtil.UpdateFilterTypeAndSelection(WindowState);
            PropertyUtil.PerformFrameActions(WindowState);

            if (PropertyUtil.ShouldRefilter(WindowState))
                PropertyUtil.Filter(WindowState);

            EditorUtil.DrawObjects(WindowState);

            WindowState.LastFrameState = WindowState.CurrentFrameState;
        }

        void OnDestroy()
        {
            EditorUtil.SaveConfigsFromFilterConfigIntoPrefs(WindowState.Config);
        }

        public void OnSelectionChange()
        {
            PropertyUtil.UpdateFilterTypeAndSelection(WindowState);
            Repaint();
        }

        void OnInspectorUpdate()
        {
            Repaint();
        }
    }

    /// <summary>
    /// Utility class to handle editor-related methods (such as drawing, opening windows, saving to EditorPrefs, etc).
    /// </summary>
    public static class EditorUtil
    {
        /// <summary>
        /// Opens a new window of the plugin.
        /// Also creates and defines the WindowState and its properties.
        /// </summary>
        public static void OpenNewWindow()
        {
            var windowState = new WindowState();

            windowState.Contents = CreateDefaultEditorContent();
            windowState.Config = CreateDefaultFilterConfig();
            LoadConfigsFromPrefsIntoFilterConfig(windowState.Config);
            windowState.LockSelection = false;
            windowState.NextSearchTime = -1;
            windowState.CurrentObjects = new List<DrawableObject>();
            windowState.CurrentFrameState = new FrameState();
            windowState.LastFrameState = new FrameState();

            var window = UnityEditor.Editor.CreateInstance<PropertyInspectorWindow>();

            window.autoRepaintOnSceneChange = true;
            window.wantsMouseEnterLeaveWindow = true;
            window.wantsMouseMove = true;
            window.titleContent = windowState.Contents.TitleContent;
            window.WindowState = windowState;
            windowState.Window = window;

            window.ShowUtility();
        }

        [MenuItem("Window/PropertyInspector window &s")]
        public static void OpenUtilityMenu()
        {
            OpenNewWindow();
        }

        /// <summary>
        /// Creates the default editor content.
        /// This will create all GUIContent to draw buttons, labels, input fields, etc.
        /// </summary>
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

        /// <summary>
        /// Creates the default filter config.
        /// </summary>
        public static FilterConfig CreateDefaultFilterConfig()
        {
            var config = new FilterConfig();
            config.FilterText = string.Empty;
            config.FilterType = FilterType.None;
            LoadConfigsFromPrefsIntoFilterConfig(config);
            return config;
        }

        /// <summary>
        /// Loads the filter configs from editor prefs.
        /// </summary>
        public static void LoadConfigsFromPrefsIntoFilterConfig(FilterConfig config)
        {
            config.MultiEdit = EditorPrefs.GetBool(Constants.MultiEditPrefsKey);
            config.HideContent = EditorPrefs.GetBool(Constants.HideContentPrefsKey);
            config.UseCustomInspector = EditorPrefs.GetBool(Constants.UseCustomInspecPrefsKey);
            config.ShowMethodButtons = EditorPrefs.GetBool(Constants.MethodButtonsPrefsKey);
        }

        /// <summary>
        /// Saves the filter configs into the editor prefs.
        /// </summary>
        public static void SaveConfigsFromFilterConfigIntoPrefs(FilterConfig config)
        {
            EditorPrefs.SetBool(Constants.MultiEditPrefsKey, config.MultiEdit);
            EditorPrefs.SetBool(Constants.HideContentPrefsKey, config.HideContent);
            EditorPrefs.SetBool(Constants.UseCustomInspecPrefsKey, config.UseCustomInspector);
            EditorPrefs.SetBool(Constants.MethodButtonsPrefsKey, config.ShowMethodButtons);
        }

        /// <summary>
        /// Draws the heade of the plugin window.
        /// </summary>
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

            var search = EditorGUILayout.TextField(window.Config.FullFilterText, (GUIStyle)"ToolbarSeachTextField", GUILayout.Width(window.Window.position.width - 25));

            if (search != window.Config.FilterText)
            {
                window.NextSearchTime = (float)EditorApplication.timeSinceStartup + .2f;
                window.Config.FullFilterText = search;
                window.Config.FilterText = search;
                window.Config.FilterTextLower = search.ToLower();
            }

            var style = "ToolbarSeachCancelButtonEmpty";
            if (!string.IsNullOrEmpty(window.Config.FullFilterText))
                style = "ToolbarSeachCancelButton";

            if (GUILayout.Button(GUIContent.none, style))
            {
                window.Config.FilterText = string.Empty;
                window.Config.FullFilterText = string.Empty;
                window.Config.FilterTextLower = string.Empty;
                GUIUtility.keyboardControl = 0;
            }
        }

        public static void DrawTopHeaderButtons(WindowState window)
        {
            var locked = GUILayout.Toggle(window.LockSelection, window.Contents.LockButton, "IN LockButton");
            if (locked != window.LockSelection)
            {
                window.LockSelection = locked;
                window.Selection = Selection.objects;
            }

            GUILayout.Space(5);

            window.CurrentFrameState.ShowHelp = GUILayout.Button(window.Contents.HelpButton, GUIStyle.none);
        }

        public static void DrawBottomHeaderToggles(WindowState window)
        {
            var multiEdit = EditorGUILayout.ToggleLeft(window.Contents.MultiEditToggle, window.Config.MultiEdit, GUILayout.MaxWidth(60));
            if (window.Config.MultiEdit != multiEdit)
                window.CurrentFrameState.ChangedToggles = true;
            window.Config.MultiEdit = multiEdit;

            var useCustomInspector = EditorGUILayout.ToggleLeft(window.Contents.UseCustomInspectorToggle, window.Config.UseCustomInspector, GUILayout.MaxWidth(105));
            if (window.Config.UseCustomInspector != useCustomInspector)
                window.CurrentFrameState.ChangedToggles = true;
            window.Config.UseCustomInspector = useCustomInspector;

            var showMethodButtons = EditorGUILayout.ToggleLeft(window.Contents.ShowMethodButtonsToggle, window.Config.ShowMethodButtons, GUILayout.MaxWidth(105));
            if (window.Config.ShowMethodButtons != showMethodButtons)
                window.CurrentFrameState.ChangedToggles = true;
            window.Config.ShowMethodButtons = showMethodButtons;

            var hideContent = EditorGUILayout.ToggleLeft(window.Contents.HideContentToggle, window.Config.HideContent, GUILayout.MaxWidth(110));
            if (window.Config.HideContent != hideContent)
                window.CurrentFrameState.ChangedToggles = true;
            window.Config.HideContent = hideContent;
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

        public static void DrawObjects(WindowState window)
        {
            EditorGUILayout.BeginVertical();

            var w = window.Window.position.width;
            var h = window.Window.position.height;
            window.CurrentFrameState.ScrollPosition = EditorGUILayout.BeginScrollView(window.LastFrameState.ScrollPosition, GUILayout.Width(w), GUILayout.Height(h - 100));

            var objects = window.CurrentObjects;
            for (int i = 0; i < objects.Count; i++)
            {
                var obj = objects[i];
                bool expanded = DrawObjectHeader(obj);

                if (expanded)
                {
                    BeginContents();
                    if (window.Config.FilterType == FilterType.None || window.Config.FilterType == FilterType.Component)
                    {
                        if (window.Config.UseCustomInspector)
                        {
                            var previousWidth = EditorGUIUtility.labelWidth;
                            EditorGUIUtility.labelWidth = 200;

                            obj.Editor.OnInspectorGUI();

                            EditorGUIUtility.labelWidth = previousWidth;
                        }
                        else
                            obj.Editor.DrawDefaultInspector();
                    }
                    else
                    {
                        for (int j = 0; j < obj.FilteredProperties.Count; j++)
                        {
                            var property = obj.FilteredProperties[j];

                            // just to make bools be draw with toggle to the left of the name
                            if (property.propertyType == SerializedPropertyType.Boolean)
                                property.boolValue = EditorGUILayout.ToggleLeft(property.displayName, property.boolValue);
                            else
                                EditorGUILayout.PropertyField(property, true);
                        }
                    }
                    EndContents();
                }

                DrawObjectFoot(obj);
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        public static void ShowHelpPopup()
        {
            EditorUtility.DisplayDialog(Constants.HelpTitle, Constants.HelpMessage, "Ok");
        }

        public static bool DrawObjectHeader(DrawableObject drawable)
        {
            var state = PropertyUtil.GetObjectHeaderState(drawable);
            var text = string.Format("<b><size=11>{0}</size></b>", drawable.ObjectIdentifier);
            if (state)
                text = "\u25BC " + text;
            else
                text = "\u25BA " + text;

            var changed = !GUILayout.Toggle(true, text, "dragtab", GUILayout.MinWidth(20f), GUILayout.ExpandWidth(true));
            if (changed)
            {
                state = !state;
                PropertyUtil.SetObjectHeaderState(drawable, state);
            }

            return state;
        }

        public static void DrawObjectFoot(DrawableObject drawable)
        {
            GUILayout.Space(3);
        }

        public static void BeginContents()
        {
            GUILayout.BeginHorizontal();
            EditorGUILayout.BeginHorizontal("textarea", GUILayout.MinHeight(10f));
            GUILayout.BeginVertical();

            GUILayout.Space(2f);
        }

        public static void EndContents()
        {
            GUILayout.Space(3f);

            GUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(3f);
            GUILayout.EndHorizontal();

            GUILayout.Space(3f);
        }
    }

    /// <summary>
    /// Class that handles property-related methods.
    /// </summary>
    public static class PropertyUtil
    {
        /// <summary>
        /// Returns true if any of the objects or components of the given window has change.
        /// Returns false otherwise.
        /// </summary>
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

        public static bool ShouldRefilter(WindowState state)
        {
            if (EditorApplication.timeSinceStartup >= state.NextSearchTime)
                return true;

            return state.CurrentFrameState.ChangedToggles;
        }

        public static void UpdateFilterTypeAndSelection(WindowState state)
        {
            var config = state.Config;
            var text = config.FilterText;

            FilterType filterType = FilterType.None;

            if (!string.IsNullOrEmpty(text))
            {
                filterType = FilterType.Contains;

                if (text.StartsWith(Constants.StartsWithPrefix))
                    filterType = FilterType.StartsWith;
                else if (text.StartsWith(Constants.StartsWithPrefix))
                    filterType = FilterType.EndsWith;
                else if (text.StartsWith(Constants.EndsWithPrefix))
                    filterType = FilterType.Match;
                else if (text.StartsWith(Constants.MatchPrefix))
                    filterType = FilterType.Type;
                else if (text.StartsWith(Constants.TypePrefix))
                    filterType = FilterType.Value;
                else if (text.StartsWith(Constants.ComponentPrefix))
                    filterType = FilterType.Component;

                // if the search text has a prefix
                if (filterType != FilterType.Contains)
                {
                    state.Config.FilterText = state.Config.FilterText.Remove(0, 2);
                    state.Config.FilterTextLower = state.Config.FilterText.ToLower();
                }
            }

            config.FilterType = filterType;

            if (!state.LockSelection)
                state.Selection = Selection.objects;
        }

        public static void PerformFrameActions(WindowState state)
        {
            var currentFrame = state.CurrentFrameState;
            if (currentFrame.ShowHelp)
                EditorUtil.ShowHelpPopup();
            else if (currentFrame.ApplyAll)
                ApplyAll(state);
            else if (currentFrame.ReverAll)
                RevertAll(state);
            else if (currentFrame.ExpandAll)
                ExpandAll(state);
            else if (currentFrame.CollapseAll)
                CollapseAll(state);
        }

        public static void Filter(WindowState state)
        {
            ClearObjects(state);

            var objects = state.Selection;
            for (int i = 0; i < objects.Length; i++)
            {
                var obj = objects[i];
                if (obj is GameObject)
                {
                    var go = obj as GameObject;
                    var components = go.GetComponents<Component>();
                    for (int j = 0; j < components.Length; j++)
                    {
                        var comp = components[j];
                        if (IsObjectFit(state, comp))
                            CreateOrUpdateDrawableObject(state, comp);
                    }
                }
                else
                {
                    if (IsObjectFit(state, obj))
                        CreateOrUpdateDrawableObject(state, obj);
                }
            }

            for (int i = 0; i < state.CurrentObjects.Count; i++)
            {
                var drawable = state.CurrentObjects[i];
                FillDrawableProperties(state, drawable);
            }
        }

        private static bool IsObjectFit(WindowState state, UnityEngine.Object obj)
        {
            switch (state.Config.FilterType)
            {
                case FilterType.None:
                case FilterType.Contains:
                case FilterType.StartsWith:
                case FilterType.EndsWith:
                case FilterType.Match:
                case FilterType.Type:
                case FilterType.Value:
                    return true;
                case FilterType.Component:
                    return obj.GetType().Name.Equals(state.Config.FilterText, StringComparison.InvariantCultureIgnoreCase);
            }

            return false;
        }

        private static void CreateOrUpdateDrawableObject(WindowState state, UnityEngine.Object obj)
        {
            var objType = obj.GetType();
            var drawables = state.CurrentObjects;
            for (int i = 0; i < drawables.Count; i++)
            {
                var drawable = drawables[i];
                if (objType == drawable.ObjectType)
                {
                    drawable.Objects.Add(obj);
                    return;
                }
            }

            var newDrawable = new DrawableObject();
            newDrawable.Objects = new List<Object>();
            newDrawable.Objects.Add(obj);
            newDrawable.FilteredProperties = new List<SerializedProperty>();
            newDrawable.ObjectIdentifier = objType.Name;
            newDrawable.ObjectType = objType;

            state.CurrentObjects.Add(newDrawable);
        }

        private static void FillDrawableProperties(WindowState state, DrawableObject drawable)
        {
            var objs = drawable.Objects.ToArray();
            drawable.SerializedObject = new SerializedObject(objs);
            drawable.Editor = UnityEditor.Editor.CreateEditor(objs);
            
            // if there's no filter, just draw the editor, no need to filter properties
            if (state.Config.FilterType == FilterType.None)
                return;

            var property = drawable.SerializedObject.GetIterator();

            if (property.NextVisible(true))
            {
                do
                {
                    if (IsPropertyFit(state, property))
                        drawable.FilteredProperties.Add(property.Copy());
                } while (property.NextVisible(false));
            }

            property.Reset();
        }

        private static bool IsPropertyFit(WindowState state, SerializedProperty property)
        {
            var config = state.Config;
            var filterText = config.FilterTextLower;
            var filterTextParts = filterText.Split(' ');

            var propertyNames = new string[2];
            propertyNames[0] = property.displayName.ToLower();
            propertyNames[1] = property.name.ToLower();

            switch (config.FilterType)
            {
                case FilterType.Contains:
                    for (int i = 0; i < propertyNames.Length; i++)
                    {
                        var propertyName = propertyNames[i];
                        for (int j = 0; j < filterTextParts.Length; j++)
                        {
                            var part = filterTextParts[j];
                            if (propertyName.Contains(part))
                                return true;
                        }
                    }
                    break;
                case FilterType.StartsWith:
                    for (int i = 0; i < propertyNames.Length; i++)
                    {
                        var propertyName = propertyNames[i];
                        if (propertyName.StartsWith(filterText))
                            return true;
                    }
                    break;
                case FilterType.EndsWith:
                    for (int i = 0; i < propertyNames.Length; i++)
                    {
                        var propertyName = propertyNames[i];
                        if (propertyName.EndsWith(filterText))
                            return true;
                    }
                    break;
                case FilterType.Match:
                    for (int i = 0; i < propertyNames.Length; i++)
                    {
                        var propertyName = propertyNames[i];
                        if (propertyName.Equals(filterText))
                            return true;
                    }
                    break;
                case FilterType.Type:
                    return property.type.Equals(filterText, StringComparison.InvariantCultureIgnoreCase);
                case FilterType.Value:
                    var value = GetValueString(property);
                    if (string.IsNullOrEmpty(value))
                        return false;
                    else
                        return value.Equals(filterText, StringComparison.InvariantCultureIgnoreCase);
                case FilterType.Component:
                    return false;
                default:
                    return false;
            }

            return false;
        }

        public static void ClearObjects(WindowState state)
        {
            state.CurrentObjects.Clear();
        }

        public static void ApplyAll(WindowState state)
        {
            // TODO
#if DEB
            Debug.Log("APPLY ALL");
#endif
        }

        public static void RevertAll(WindowState state)
        {
            // TODO
#if DEB
            Debug.Log("REVERT ALL");
#endif
        }

        public static void ExpandAll(WindowState state)
        {
            for (int i = 0; i < state.CurrentObjects.Count; i++)
            {
                var current = state.CurrentObjects[i];
                SetObjectHeaderState(current, true);
            }
        }

        public static void CollapseAll(WindowState state)
        {
            for (int i = 0; i < state.CurrentObjects.Count; i++)
            {
                var current = state.CurrentObjects[i];
                SetObjectHeaderState(current, false);
            }
        }

        public static bool GetObjectHeaderState(DrawableObject drawable)
        {
            var entryName = GetObjectHeaderPrefsKey(drawable);
            return EditorPrefs.GetBool(entryName, false);
        }

        public static void SetObjectHeaderState(DrawableObject drawable, bool expanded)
        {
            var entryName = GetObjectHeaderPrefsKey(drawable);
            EditorPrefs.SetBool(entryName, expanded);
        }

        public static string GetObjectHeaderPrefsKey(DrawableObject drawable)
        {
            return string.Format(Constants.ObjectHeaderPrefixFormat, drawable.ObjectIdentifier);
        }

        private static string GetValueString(SerializedProperty property)
        {
            switch (property.propertyType)
            {
                case SerializedPropertyType.Integer:
                    return property.intValue.ToString();
                case SerializedPropertyType.Boolean:
                    return property.boolValue.ToString();
                case SerializedPropertyType.Float:
                    return property.floatValue.ToString();
                case SerializedPropertyType.String:
                    return property.stringValue;
                case SerializedPropertyType.Color:
                    return property.colorValue.ToString();
                case SerializedPropertyType.ObjectReference:
                    if (property.objectReferenceValue)
                        return property.objectReferenceValue.GetType().Name;
                    else
                        return null;
                case SerializedPropertyType.LayerMask:
                    return property.ToString();
                case SerializedPropertyType.Enum:
                    if (property.enumValueIndex < 0 || property.enumValueIndex > property.enumNames.Length)
                        return null;
                    else
                        return property.enumNames[property.enumValueIndex];
                case SerializedPropertyType.Vector2:
                    return property.vector2Value.ToString();
                case SerializedPropertyType.Vector3:
                    return property.vector3Value.ToString();
                case SerializedPropertyType.Vector4:
                    return property.vector4Value.ToString();
                case SerializedPropertyType.Rect:
                    return property.rectValue.ToString();
                case SerializedPropertyType.ArraySize:
                    return null;
                case SerializedPropertyType.Character:
                    return property.ToString();
                case SerializedPropertyType.AnimationCurve:
                    return property.animationCurveValue.ToString();
                case SerializedPropertyType.Bounds:
                    return property.boundsValue.ToString();
                case SerializedPropertyType.Gradient:
                    return property.ToString();
                case SerializedPropertyType.Quaternion:
                    return property.quaternionValue.ToString();
                case SerializedPropertyType.ExposedReference:
                    if (property.exposedReferenceValue)
                        return property.exposedReferenceValue.ToString();
                    else
                        return null;
                case SerializedPropertyType.FixedBufferSize:
                    return null;
                default:
                    return null;
            }
        }
    }
}