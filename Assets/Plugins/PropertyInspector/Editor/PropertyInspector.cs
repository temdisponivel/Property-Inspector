using System;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEditor;
using Object = UnityEngine.Object;
using Random = System.Random;

[ExecuteInEditMode]
public class PropertyInspector : EditorWindow, IHasCustomMenu
{
    #region Inner type

    /// <summary>
    /// Enumerator used to identify what type of search is being done.
    /// </summary>
    private enum SearchPattern
    {
        StartsWith,
        EndsWith,
        Contains,
        Match,
        Type,
        Value,
    }

    /// <summary>
    /// Class that represents a object that we will draw into the screen.
    /// </summary>
    private class DrawableProperty
    {
        public DrawableProperty()
        {
            UnityObjects = new List<Object>();
            Childs = new List<DrawableProperty>();
            PropertiesPaths = new HashSet<string>();
        }

        public int Id
        {
            get { return UnityObjects[0].GetInstanceID(); }
        }
        public int ButtonsId { get { return Id * 99999; } }
        public List<Object> UnityObjects { get; set; }
        public Type Type { get; set; }
        public SerializedObject Object { get; set; }
        public HashSet<string> PropertiesPaths { get; set; }
        public List<DrawableProperty> Childs { get; set; }
        public bool HasAppliableChanges { get; set; }
        public Editor CustomEditor { get; set; }

        public bool HasDestroyedObject()
        {
            if (Object.targetObjects.Length > 0)
            {
                for (int i = 0; i < Object.targetObjects.Length; i++)
                {
                    if (!Object.targetObjects[i])
                        return true;
                }
            }
            return !Object.targetObject;
        }
    }

    #endregion

    #region Properties

    private readonly List<DrawableProperty> _drawable = new List<DrawableProperty>();

    private const string SearchFieldName = "PISearchQuery";
    private const string InspectorModeKey = "PIInspectorMode";
    private const string MultipleEditKey = "PIMultiEdit";
    private const string UseCustomInspectorsKey = "PIUseCustomInspectors";
    private const string ShowMethodsAsButtons = "PIShowMethodsAsButtons";

    private bool _openedAsUtility { get; set; }
    private Vector2 _scrollPosition = Vector2.zero;
    private Rect _lastDrawPosition;
    private bool _focus = false;
    private string _lastSearchedQuery = string.Empty;
    private string _currentSearchedQuery = string.Empty;
    private double _startSearchTime;
    private double _timeToSearchAgain;
    private bool _searching;
    private bool _expandAll;
    private bool _collapseAll;
    private bool _multipleEdit;
    private bool _locked;
    private bool _inspectorMode;
    private bool _useCustomInspectors;
    private bool _applyAll;
    private bool _revertAll;
    private bool _showMethodAsButtons;
    private readonly Dictionary<int, bool> _headersState = new Dictionary<int, bool>();
    private List<Object> _rightClickedObjects;

    private const string _multiEditHeaderFormat = "{0} ({1})";

    private readonly Version Version = new Version(1, 0, 0, 4);

    private string _currentSearchedAsLower
    {
        get
        {
            if (SearchPatternToUse != SearchPattern.Contains)
                return _currentSearchedQuery.Substring(2).ToLower();

            return _currentSearchedQuery.ToLower();
        }
    }

    private bool _forcedShow
    {
        get { return _inspectorMode && string.IsNullOrEmpty(_currentSearchedQuery); }
    }

    private bool _shouldUseCustomEditors
    {
        get { return _inspectorMode && string.IsNullOrEmpty(_currentSearchedQuery); }
    }

    private SearchPattern SearchPatternToUse
    {
        get
        {
            if (_currentSearchedQuery.StartsWith("s:", StringComparison.OrdinalIgnoreCase))
                return SearchPattern.StartsWith;
            else if (_currentSearchedQuery.StartsWith("e:", StringComparison.OrdinalIgnoreCase))
                return SearchPattern.EndsWith;
            else if (_currentSearchedQuery.StartsWith("m:", StringComparison.OrdinalIgnoreCase))
                return SearchPattern.Match;
            else if (_currentSearchedQuery.StartsWith("t:", StringComparison.OrdinalIgnoreCase))
                return SearchPattern.Type;
            else if (_currentSearchedQuery.StartsWith("v:", StringComparison.OrdinalIgnoreCase))
                return SearchPattern.Value;
            else
                return SearchPattern.Contains;
        }
    }

    private Object[] _lockedObjects;

    public Object[] _objectsToFilter
    {
        get
        {
            if (_locked)
            {
                UpdateLockedObject();
                return _lockedObjects;
            }

            return Selection.objects;
        }
    }

    private static GUIContent _highlightGuiContentCache;

    private static GUIContent _highlightGUIContent
    {
        get
        {
            if (_highlightGuiContentCache == null)
            {
                var textToLoad = "icons/UnityEditor.HierarchyWindow.png";
                if (EditorGUIUtility.isProSkin)
                    textToLoad = "icons/d_UnityEditor.HierarchyWindow.png";

                _highlightGuiContentCache = new GUIContent(EditorGUIUtility.Load(textToLoad) as Texture2D,
                    "Select object");
            }
            return _highlightGuiContentCache;
        }
    }

    private static GUIContent _openScriptContentCache;

    private static GUIContent _openScriptContent
    {
        get
        {
            if (_openScriptContentCache == null)
            {
                var textToLoad = "icons/UnityEditor.ConsoleWindow.png";
                _openScriptContentCache = new GUIContent(EditorGUIUtility.Load(textToLoad) as Texture2D, "Edit script");
            }
            return _openScriptContentCache;
        }
    }

    private static GUIContent _titleGUIContentCache;

    private static GUIContent _titleGUIContent
    {
        get
        {
            if (_titleGUIContentCache == null)
            {
                var textToLoad = "icons/ViewToolZoom.png";
                if (EditorGUIUtility.isProSkin)
                    textToLoad = "icons/d_ViewToolZoom.png";

                _titleGUIContentCache = new GUIContent("Property Inspector",
                    EditorGUIUtility.Load(textToLoad) as Texture2D);
            }

            return _titleGUIContentCache;
        }
    }

    private static GUIContent _tabTitleGUIContentCache;

    private static GUIContent _tabTitleGUIContent
    {
        get
        {
            if (_tabTitleGUIContentCache == null)
            {
                var textToLoad = "icons/ViewToolZoom.png";
                if (EditorGUIUtility.isProSkin)
                    textToLoad = "icons/d_ViewToolZoom.png";

                _tabTitleGUIContentCache = new GUIContent("Prop Insp", EditorGUIUtility.Load(textToLoad) as Texture2D);
            }

            return _tabTitleGUIContentCache;
        }
    }

    private static GUIContent _helpGUIContentCache;

    private static GUIContent _helpGUIContent
    {
        get
        {
            if (_helpGUIContentCache == null)
                _helpGUIContentCache = new GUIContent(EditorGUIUtility.Load("icons/_Help.png") as Texture2D, "Show Help");

            return _helpGUIContentCache;
        }
    }

    private static GUIContent _collapseGUIContentCache;

    private static GUIContent _collapseGUIContent
    {
        get
        {
            if (_collapseGUIContentCache == null)
            {
                var textToLoad = "icons/winbtn_win_min.png";
                if (EditorGUIUtility.isProSkin)
                    textToLoad = "icons/d_winbtn_win_min.png";
                _collapseGUIContentCache = new GUIContent(EditorGUIUtility.Load(textToLoad) as Texture2D,
                    tooltip: "Collapse all");
            }

            return _collapseGUIContentCache;
        }
    }

    private static GUIContent __expandGUIContentCache;

    private static GUIContent _expandGUIContent
    {
        get
        {
            if (__expandGUIContentCache == null)
            {
                var textToLoad = "icons/winbtn_win_max.png";
                if (EditorGUIUtility.isProSkin)
                    textToLoad = "icons/d_winbtn_win_max.png";
                __expandGUIContentCache = new GUIContent(EditorGUIUtility.Load(textToLoad) as Texture2D,
                    tooltip: "Expand all");
            }

            return __expandGUIContentCache;
        }
    }

    #endregion

    #region Init

    [MenuItem("Window/Property Inspector Popup &f")]
    private static void Init()
    {
        var window = CreateInstance<PropertyInspector>();
        window._openedAsUtility = true;
        SetupInfo(window);
        window.ShowUtility();
    }

    [MenuItem("Window/Property Inspector Window")]
    private static void InitWindow()
    {
        var window = CreateInstance<PropertyInspector>();
        SetupInfo(window);
        window.Show();
    }


    /// <summary>
    /// Setup and load initial information.
    /// </summary>
    /// <param name="window"></param>
    static void SetupInfo(PropertyInspector window)
    {
        window.titleContent = _tabTitleGUIContent;
        window._focus = true;
        window.wantsMouseMove = true;
        window.autoRepaintOnSceneChange = true;
        //window.minSize = new Vector2(400, window.minSize.y);

        window._inspectorMode = EditorPrefs.GetBool(InspectorModeKey + window._openedAsUtility, false);
        window._multipleEdit = EditorPrefs.GetBool(MultipleEditKey + window._openedAsUtility, false);
        window._useCustomInspectors = EditorPrefs.GetBool(UseCustomInspectorsKey + window._openedAsUtility, false);
        window._showMethodAsButtons = EditorPrefs.GetBool(ShowMethodsAsButtons + window._openedAsUtility, false);

        window.FilterSelected();
    }

    #endregion

    #region Title

    /// <summary>
    /// Draw the header with search field, check boxes and so on
    /// </summary>
    private void DrawSearchField()
    {
        bool filter = false;

        GUI.SetNextControlName(SearchFieldName);

        GUILayout.BeginVertical("In BigTitle");
        EditorGUILayout.BeginVertical();
        GUILayout.Space(5);
        EditorGUILayout.BeginHorizontal();

        EditorGUILayout.LabelField(_titleGUIContent);

        GUILayout.FlexibleSpace();

        if (_openedAsUtility)
        {
            var locked = GUILayout.Toggle(_locked, GUIContent.none, "IN LockButton");
            if (locked != _locked)
            {
                _locked = locked;
                _lockedObjects = Selection.objects;
                FilterSelected();
            }

            GUILayout.Space(5);
        }

        if (GUILayout.Button(_helpGUIContent, GUIStyle.none))
        {
            ShowHelp();
        }

        EditorGUILayout.EndHorizontal();
        EditorGUILayout.BeginVertical();
        GUILayout.Space(5);
        EditorGUILayout.EndVertical();

        EditorGUILayout.BeginHorizontal();

        GUI.SetNextControlName(SearchFieldName);

        var search = EditorGUILayout.TextField(_currentSearchedQuery, (GUIStyle)"ToolbarSeachTextField", GUILayout.Width(position.width - 25));

        if (search != _currentSearchedQuery)
        {
            _timeToSearchAgain = EditorApplication.timeSinceStartup + .2f;
            _currentSearchedQuery = search;
        }

        var style = "ToolbarSeachCancelButtonEmpty";
        if (!string.IsNullOrEmpty(_currentSearchedQuery))
            style = "ToolbarSeachCancelButton";

        if (GUILayout.Button(GUIContent.none, style))
        {
            _currentSearchedQuery = string.Empty;
            GUIUtility.keyboardControl = 0;
        }

        EditorGUILayout.EndHorizontal();

        GUILayout.Space(5);

        EditorGUILayout.BeginHorizontal();

        var edit = EditorGUILayout.ToggleLeft(new GUIContent("Multi-edit", tooltip: "Edit multiple objects as one"), _multipleEdit, GUILayout.MaxWidth(70));
        var inspectorMode = EditorGUILayout.ToggleLeft(new GUIContent("Inspector mode", tooltip: "Show all properties when search query is empty (slow when viewing numerous objects without query)"), _inspectorMode, GUILayout.MaxWidth(105));
        var customInspector = EditorGUILayout.ToggleLeft(new GUIContent("Custom Inspec", tooltip: "Show objects and components using its custom (or default) inspectors"), _useCustomInspectors, GUILayout.MaxWidth(105));
        var showMethodsAsButtons = EditorGUILayout.ToggleLeft(new GUIContent("Methods buttons", tooltip: "Show object's methods as buttons at the end of the inspector"), _showMethodAsButtons, GUILayout.MaxWidth(110));

        GUILayout.FlexibleSpace();

        var changed = false;
        for (int i = 0; i < _drawable.Count; i++)
        {
            if (_drawable[i].HasAppliableChanges)
            {
                changed = true;
                break;
            }
        }

        if (!changed)
            GUI.enabled = false;
        _applyAll = GUILayout.Button(new GUIContent("Apply all", tooltip: changed ? "Apply all instance changes to prefabs" : "There's no changes to apply"), (GUIStyle)"miniButtonLeft");
        _revertAll = GUILayout.Button(new GUIContent("Revert all", tooltip: changed ? "Revert all instance changes to prefabs" : "There's no changes to revert"), (GUIStyle)"miniButtonRight");
        if (!changed)
            GUI.enabled = true;

        _expandAll = GUILayout.Button(_expandGUIContent, "miniButtonLeft");
        _collapseAll = GUILayout.Button(_collapseGUIContent, "miniButtonRight");

        EditorGUILayout.EndHorizontal();
        GUILayout.Space(5);
        EditorGUILayout.EndVertical();
        GUILayout.EndVertical();

        if (edit != _multipleEdit)
        {
            filter = true;
            EditorPrefs.SetBool(MultipleEditKey + _openedAsUtility, edit);
        }
        if (inspectorMode != _inspectorMode)
        {
            filter = true;
            EditorPrefs.SetBool(InspectorModeKey + _openedAsUtility, inspectorMode);
        }
        if (customInspector != _useCustomInspectors)
        {
            filter = true;
            EditorPrefs.SetBool(UseCustomInspectorsKey + _openedAsUtility, customInspector);
        }

        _inspectorMode = inspectorMode;
        _multipleEdit = edit;
        _useCustomInspectors = customInspector;
        _showMethodAsButtons = showMethodsAsButtons;

        if (filter)
            FilterSelected();
    }

    #endregion

    #region Unity event

    void Update()
    {
        if (_searching)
            return;

        if (_lastSearchedQuery != _currentSearchedQuery)
        {
            if (EditorApplication.timeSinceStartup >= _timeToSearchAgain || string.IsNullOrEmpty(_currentSearchedQuery))
            {
                FilterSelected();
                _lastSearchedQuery = _currentSearchedQuery;
            }
        }
    }

    void OnSelectionChange()
    {
        // if locked, doesn't refilter because nothing really changed
        if (_locked)
            ValidaIfCanApplyAll();
        else
            FilterSelected();

        Repaint();
    }

    void OnInspectorUpdate()
    {
        Repaint();
    }

    void OnFocus()
    {
        // refilter objects because when we are not on focus, we don't receive the OnSelectionChange event
        // so we need to get the selected objects again
        FilterSelected();
        Repaint();
        _focus = true;
    }

    /// <summary>
    ///  Show padlock button on menu
    /// </summary>
    void ShowButton(Rect position)
    {
        var locked = GUI.Toggle(position, _locked, GUIContent.none, "IN LockButton");

        if (locked != _locked)
        {
            _locked = locked;
            _lockedObjects = Selection.objects;
            FilterSelected();
        }
    }

    #endregion

    #region GUI

    private void OnGUI()
    {
        ApplyRevertAllAndFocus();

        // Update serializable objects with the actual object information
        UpdateAllProperties();

        DrawSearchField();

        // focus search box if it's necessary
        if (_focus)
        {
            EditorGUI.FocusTextInControl(SearchFieldName);
            _focus = false;
        }

        EditorGUILayout.BeginVertical();
        _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, GUILayout.Width(position.width), GUILayout.Height(position.height - 100));

        if (_expandAll)
            ExpandAll();
        else if (_collapseAll)
            CollapseAll();

        // Iterate through all drawables and draw them into the screen
        // This drawable have already been filtereds and are ready to draw
        for (int i = 0; i < _drawable.Count; i++)
        {
            var current = _drawable[i];

            if (DrawObjectHeader(current, false))
            {
                BeginContents();
                {
                    DrawObject(current);

                    // do basically the same we just did but for every child of this object
                    for (int j = 0; j < current.Childs.Count; j++)
                    {
                        var currentChild = current.Childs[j];

                        if (DrawObjectHeader(currentChild, true))
                        {
                            BeginContents();

                            DrawObject(currentChild);

                            DrawMethodButtons(currentChild);

                            EndContents();

                            if (ShouldSkipDrawing())
                                break;
                        }
                    }

                    DrawMethodButtons(current);
                }
                EndContents();

                if (ShouldSkipDrawing())
                    break;
            }
        }

        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }

    /// <summary>
    /// Helper function that calls apply or revert
    /// Or focus the cursor on the search
    /// If needed. 
    /// </summary>
    private void ApplyRevertAllAndFocus()
    {
        // We can only change our objects inside layout event
        if (Event.current.type == EventType.Layout)
        {
            if (_applyAll)
            {
                ApplyAll();
                _applyAll = false;
            }
            else if (_revertAll)
            {
                RevertAll();
                _revertAll = false;
            }

            ValidaIfCanApplyAll();
        }

        if (Event.current.type == EventType.KeyUp)
        {
            if (Event.current.keyCode == KeyCode.F && Event.current.control)
            {
                _focus = true;
            }
        }
    }

    private bool DrawObjectHeader(DrawableProperty property, bool useTypeAsName)
    {
        var typeName = string.Empty;
        if (property.Type != null)
            typeName = property.Type.Name;
        else
            typeName = property.UnityObjects[0].GetType().Name;

        // if it's editing multiple objects, change name
        name = string.Format(_multiEditHeaderFormat, typeName, property.Object.targetObjects.Length);
        if (!_multipleEdit)
        {
            if (useTypeAsName)
                name = property.UnityObjects[0].GetType().Name;
            else
                name = property.UnityObjects[0].name;
        }

        // Create the callback for the object header
        // If we pass the callback as something not null
        // a button for the callback will be displayed in the header
        var buttonCallback = GetObjectToHight(property);
        var openScriptCallback = GetOpenScriptCallback(property);
        Action applyCallback = null;
        Action revertCallback = null;
        Action collapseChilds = null;
        Action expandChilds = null;

        if (property.HasAppliableChanges)
        {
            applyCallback = () => ApplyChangesToPrefab(property);
            revertCallback = () => RevertChangesToPrefab(property);
        }

        if (property.Childs.Count > 0 || _showMethodAsButtons)
        {
            collapseChilds = () => SetStateInChild(property, false);
            expandChilds = () => SetStateInChild(property, true);
        }

        return (DrawHeader(name, property, buttonCallback, applyCallback, revertCallback, openScriptCallback, collapseChilds, expandChilds));
    }

    private void DrawObject(DrawableProperty property)
    {
        var serializedObjectChild = property.Object;

        EditorGUI.BeginChangeCheck();

        // if we should use custom inspectors, use it
        // otherwise, draw every property one by one
        if (_shouldUseCustomEditors)
        {
            // Custom inspector will be draw oddly without this two line below
            // Labels will be very wide and fields will be right align
            // With this lines it looks nicer

            var previousWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = position.width / 2;

            // this should not happen, but some reason it does
            if (property.CustomEditor != null)
            {
                if (_useCustomInspectors)
                    property.CustomEditor.OnInspectorGUI();
                else
                    property.CustomEditor.DrawDefaultInspector();
            }
            EditorGUIUtility.labelWidth = previousWidth;
        }
        else
        {
            foreach (var serializedProperty in property.PropertiesPaths)
            {
                var child = serializedObjectChild.FindProperty(serializedProperty);
                if (child == null)
                    continue;
                switch (child.propertyType)
                {
                    case SerializedPropertyType.Boolean:
                        child.boolValue = EditorGUILayout.ToggleLeft(child.displayName, child.boolValue);
                        break;
                    default:
                        EditorGUILayout.PropertyField(child, true);
                        break;
                }
            }
        }

        if (EditorGUI.EndChangeCheck())
            serializedObjectChild.ApplyModifiedProperties();
    }

    #endregion

    #region Filter

    /// <summary>
    /// Entry point for filtering.
    /// </summary>
    private void FilterSelected()
    {
        _drawable.Clear();

        if (_shouldUseCustomEditors)
            FilterForCustomInspector();
        else if (_multipleEdit)
            FilterMultiple();
        else
            FilterSingles();

        ValidaIfCanApplyAll();
    }

    /// <summary>
    /// Filter used for when we should use custom inspectors
    /// </summary>
    private void FilterForCustomInspector()
    {
        _searching = true;

        var objects = _objectsToFilter;

        // Dictionary that will group drawables by their type
        Dictionary<Type, DrawableProperty> drawables = null;
        if (_multipleEdit)
            drawables = new Dictionary<Type, DrawableProperty>();

        // Iterate through all selected objects
        for (int i = objects.Length - 1; i >= 0; i--)
        {
            var currentObject = objects[i];

            DrawableProperty drawable = null;

            // if we are multiediting, cache the current drawable inside the dictionary
            // if not, just add to the global list
            if (_multipleEdit)
            {
                if (!drawables.TryGetValue(currentObject.GetType(), out drawable))
                    drawables[currentObject.GetType()] = drawable = new DrawableProperty();

                drawable.UnityObjects.Add(currentObject);
            }
            else
            {
                drawable = new DrawableProperty();
                drawable.UnityObjects.Add(currentObject);
                drawable.Object = new SerializedObject(currentObject);
                _drawable.Add(drawable);
            }

            // Go through all components if it's a game object
            var currentGameObject = currentObject as GameObject;
            if (currentGameObject != null)
            {
                var components = currentGameObject.GetComponents<Component>();

                for (int j = 0; j < components.Length; j++)
                {
                    // Create the child drawable
                    DrawableProperty childDrawable = null;
                    var currentChildObject = components[j];

                    // If it's multi editing, cache the drawable inside the dictionary
                    // if it's not multi editing, add to the child list
                    if (_multipleEdit)
                    {
                        if (!drawables.TryGetValue(currentChildObject.GetType(), out childDrawable))
                            drawables[currentChildObject.GetType()] = childDrawable = new DrawableProperty();

                        childDrawable.UnityObjects.Add(currentChildObject);
                    }
                    else
                    {
                        childDrawable = new DrawableProperty();
                        childDrawable.UnityObjects.Add(currentChildObject);
                        childDrawable.Object = new SerializedObject(currentChildObject);
                        drawable.Childs.Add(childDrawable);
                    }
                }
            }
        }

        if (_multipleEdit)
        {
            PopuplateDrawablesFromDictionary(drawables);
        }

        CreateEditorForAll();

        _searching = false;
    }

    /// <summary>
    /// Filter in single mode (not edit mode).
    /// </summary>
    private void FilterSingles()
    {
        var searchAsLow = _currentSearchedAsLower;
        _startSearchTime = EditorApplication.timeSinceStartup;
        _searching = true;
        bool isPath = _currentSearchedQuery.Contains('.');
        var objects = _objectsToFilter;

        // Iterate through all selected objects
        for (int i = objects.Length - 1; i >= 0; i--)
        {
            var currentObject = objects[i];
            var serializedObject = new SerializedObject(currentObject);
            var iterator = serializedObject.GetIterator();

            // After creating a SerializedObject for current object, filter its properties
            // the last parameter tells the method to not actually filter, but rather just create a drawable property for us
            var drawable = FilterObject(null, currentObject, searchAsLow, isPath, false);

            // if we are not in editor mode and the search is empty, add the object
            // we do this because we want to show what objects are seleted when there's no search
            // otherwise, since there's no search and are not in inspector mode, this object would not be shown to user
            if (string.IsNullOrEmpty(_currentSearchedQuery) && !_inspectorMode)
            {
                _drawable.Add(drawable);
                continue;
            }
            else
            {
                // actually filter properties
                FilterProperties(null, drawable, serializedObject, iterator, searchAsLow, isPath);
            }

            // show a progress bar if needed
            if (HandleProgressBar(i / objects.Length))
                break;

            // Go through all components if it's a game object
            var currentGameObject = currentObject as GameObject;
            if (currentGameObject != null)
            {
                var components = currentGameObject.GetComponents<Component>();

                for (int j = 0; j < components.Length; j++)
                {
                    // if it's not a Transform - this is necessary because - for some reason - Unity doesn't like to use PropertyField with transforms
                    // Filter and add the resulting drawable property as child of the game object drawable property
                    FilterObject(drawable, components[j], searchAsLow, isPath);
                }
            }

            _drawable.Add(drawable);

            if (HandleProgressBar(i / objects.Length))
                break;
        }

        _searching = false;

        EditorUtility.ClearProgressBar();
    }

    /// <summary>
    /// Filter for multi-edit.
    /// </summary>
    private void FilterMultiple()
    {
        // This search is basically the same as FilterSingles, except for some miner differences
        // I will comment only on those differences
        var searchAsLow = _currentSearchedAsLower;
        _startSearchTime = EditorApplication.timeSinceStartup;
        _searching = true;
        bool isPath = _currentSearchedQuery.Contains('.');

        Dictionary<Type, DrawableProperty> drawables = new Dictionary<Type, DrawableProperty>();

        var objects = _objectsToFilter;

        for (int i = objects.Length - 1; i >= 0; i--)
        {
            var currentObject = objects[i];
            var serializedObject = new SerializedObject(currentObject);
            var iterator = serializedObject.GetIterator();

            var drawable = FilterObject(null, currentObject, searchAsLow, isPath, false);

            bool ignorePaths = false;
            if (!string.IsNullOrEmpty(_currentSearchedQuery) || _inspectorMode)
            {
                FilterProperties(null, drawable, serializedObject, iterator, searchAsLow, isPath);
            }
            else
            {
                ignorePaths = true;
            }

            // Add all objects and properties to the list of drawables 
            // this list is used to cache all properties and objects that will be drawn
            // we will go through this list later and contruct our actual drawable properties
            AddObjectsAndProperties(drawables, drawable, currentObject, ignorePaths);

            if (string.IsNullOrEmpty(_currentSearchedQuery) && !_inspectorMode)
                continue;

            if (HandleProgressBar(i / objects.Length))
                break;

            var currentGameObject = currentObject as GameObject;
            if (currentGameObject != null)
            {
                var components = currentGameObject.GetComponents<Component>();

                for (int j = 0; j < components.Length; j++)
                {
                    var drawableChild = FilterObject(drawable, components[j], searchAsLow, isPath);
                    AddObjectsAndProperties(drawables, drawableChild, components[j]);
                }
            }
        }

        PopuplateDrawablesFromDictionary(drawables);

        _searching = false;

        EditorUtility.ClearProgressBar();
    }

    private void PopuplateDrawablesFromDictionary(Dictionary<Type, DrawableProperty> drawables)
    {
        // Go through the list of drawables
        // this list is bassicaly the same list that FilterSingles would return but with multiple objects inside SerializedObjects
        foreach (var drawableProperty in drawables)
        {
            // Recontruct the drawable property with all objects that share the same type and have a property to show
            DrawableProperty currentDrawableProperty = new DrawableProperty()
            {
                Object = new SerializedObject(drawableProperty.Value.UnityObjects.ToArray()),
                UnityObjects = drawableProperty.Value.UnityObjects,
                Type = drawableProperty.Key,
                PropertiesPaths = drawableProperty.Value.PropertiesPaths,
            };

            foreach (var propertiesPath in currentDrawableProperty.PropertiesPaths)
            {
                currentDrawableProperty.PropertiesPaths.Add(propertiesPath);
            }

            _drawable.Add(currentDrawableProperty);
        }
    }

    #endregion

    #region Filter properties

    /// <summary>
    /// Helper function to create a drawable property and filter its properties
    /// </summary>
    private DrawableProperty FilterObject(DrawableProperty father, Object uObject, string search, bool isPath, bool filter = true)
    {
        var childSerializedObject = new SerializedObject(uObject);
        var childIterator = childSerializedObject.GetIterator();

        var drawableChild = new DrawableProperty();
        drawableChild.UnityObjects.Add(uObject);
        drawableChild.Object = childSerializedObject;

        if (filter)
            FilterProperties(father, drawableChild, childSerializedObject, childIterator, search, isPath);

        return drawableChild;
    }

    /// <summary>
    /// Filter properties of a drawable property
    /// </summary>
    private void FilterProperties(DrawableProperty father, DrawableProperty child, SerializedObject serializedObject, SerializedProperty iterator, string search, bool isPath)
    {
        bool add = false;

        // Get the next property on this level
        iterator.Reset();
        while (iterator.NextVisible(true))
        {
            // if this drawable already have this property saved
            if (child.PropertiesPaths.Contains(iterator.propertyPath))
                continue;

            // See if the name of the property match the search
            SerializedProperty property;
            if (Compare(iterator, search, isPath))
            {
                property = serializedObject.FindProperty(iterator.propertyPath);
                if (property == null)
                    continue;

                // add the property to the drawable property
                add = true;
                child.PropertiesPaths.Add(property.propertyPath);
            }

            if (!isPath)
                continue;

            // if the property is a path, look for that propety using the path typed
            property = serializedObject.FindProperty(_currentSearchedQuery);
            if (property != null)
            {
                if (child.PropertiesPaths.Contains(property.propertyPath))
                    continue;

                child.PropertiesPaths.Add(property.propertyPath);
                add = true;
            }
        }

        if (add && father != null)
            father.Childs.Add(child);
    }

    /// <summary>
    /// Add obect to a existing drawable property.
    /// Update a special drawable (drawable that have all objects of a specific type)
    /// If there's such special drawable, just add the new object to it and the properties to show
    /// If not, create one and do the same
    /// </summary>
    private void AddObjectsAndProperties(Dictionary<Type, DrawableProperty> drawables, DrawableProperty drawable, Object currentObject, bool ignorePaths = false)
    {
        if (!ignorePaths && drawable.PropertiesPaths.Count == 0 && drawable.Childs.Count == 0)
            return;

        DrawableProperty drawableType;
        if (!drawables.TryGetValue(currentObject.GetType(), out drawableType))
            drawables[currentObject.GetType()] = (drawableType = new DrawableProperty());

        drawableType.UnityObjects.Add(currentObject);

        if (ignorePaths)
            return;

        foreach (var propertiesPath in drawable.PropertiesPaths)
            drawableType.PropertiesPaths.Add(propertiesPath);
    }

    #endregion

    #region Apply/revert

    /// <summary>
    /// Helper function to call ApplyChangesToPrefab on all drawables
    /// </summary>
    private void ApplyAll()
    {
        for (int i = 0; i < _drawable.Count; i++)
        {
            var current = _drawable[i];
            if (current.HasAppliableChanges)
                ApplyChangesToPrefab(current);
        }
    }

    /// <summary>
    /// Helper to call ValidateIfCanApply on all drawables
    /// </summary>
    private void ValidaIfCanApplyAll()
    {
        for (int i = 0; i < _drawable.Count; i++)
        {
            ValidateIfCanApply(_drawable[i]);
        }
    }

    /// <summary>
    /// Validate if a property has any change that may be applied (or reverted) to its prefab.
    /// </summary>
    bool ValidateIfCanApply(DrawableProperty property)
    {
        property.HasAppliableChanges = false;

        if (_shouldUseCustomEditors)
        {
            var iterator = property.Object.GetIterator();
            while (iterator.Next(true))
            {
                if (iterator.isInstantiatedPrefab && iterator.prefabOverride)
                {
                    property.HasAppliableChanges = true;
                    break;
                }
            }
        }
        else
        {
            foreach (var propertiesPath in property.PropertiesPaths)
            {
                var currentProperty = property.Object.FindProperty(propertiesPath);

                // this current property is null when the path points
                // to a index on a array (say 2) and we are editing multiple object
                // if one of the objects doesn't have a index 2 in its array, the property will not be found
                if (currentProperty == null)
                    continue;

                if (currentProperty.isInstantiatedPrefab && currentProperty.prefabOverride)
                {
                    property.HasAppliableChanges = true;
                    break;
                }
            }
        }

        bool childResults = false;
        for (int i = 0; i < property.Childs.Count; i++)
        {
            childResults |= ValidateIfCanApply(property.Childs[i]);
        }

        property.HasAppliableChanges |= childResults;

        return property.HasAppliableChanges;
    }

    /// <summary>
    /// Apply all changes made on that drawable property to the prefabs
    /// connected to the objects inside the drawable property
    /// </summary>
    void ApplyChangesToPrefab(DrawableProperty property)
    {
        List<Object> objects = new List<Object>();

        objects.AddRange(property.UnityObjects);

        for (int i = 0; i < objects.Count; i++)
        {
            var instance = objects[i] as GameObject;
            if (instance == null)
            {
                var component = objects[i] as Component;
                if (component != null)
                    instance = component.gameObject;

                if (instance == null)
                    continue;
            }

            var instanceRoot = PrefabUtility.FindRootGameObjectWithSameParentPrefab(instance);
            var targetPrefab = PrefabUtility.GetPrefabParent(instanceRoot);

            if (targetPrefab == null)
                return;

            PrefabUtility.ReplacePrefab(
                instanceRoot,
                targetPrefab,
                ReplacePrefabOptions.ConnectToPrefab
            );
        }

        property.HasAppliableChanges = false;
        GUIUtility.keyboardControl = 0;
    }

    /// <summary>
    /// Helper function to call RevertChangesToPrefab on all drawables
    /// </summary>
    private void RevertAll()
    {
        for (int i = 0; i < _drawable.Count; i++)
        {
            var current = _drawable[i];
            if (current.HasAppliableChanges)
                RevertChangesToPrefab(current);
        }
    }

    /// <summary>
    /// Revert any changes made on that drawable to the prefabs connected
    /// to the objects inside that drawable property
    /// </summary>
    void RevertChangesToPrefab(DrawableProperty property)
    {
        List<Object> objects = new List<Object>();

        objects.AddRange(property.UnityObjects);

        for (int i = 0; i < objects.Count; i++)
        {
            var instance = objects[i] as GameObject;
            if (instance == null)
            {
                var component = objects[i] as Component;
                if (component != null)
                    instance = component.gameObject;

                if (instance == null)
                    continue;
            }

            var instanceRoot = PrefabUtility.FindRootGameObjectWithSameParentPrefab(instance);
            var targetPrefab = PrefabUtility.GetPrefabParent(instanceRoot);

            if (targetPrefab == null)
                return;

            PrefabUtility.RevertPrefabInstance(instanceRoot);
        }

        property.HasAppliableChanges = false;
        GUIUtility.keyboardControl = 0;
    }

    #endregion

    #region Compare

    /// <summary>
    /// See if a property match a search query
    /// </summary>
    public bool Compare(SerializedProperty property, string searchAsLow, bool isPath)
    {
        if (_forcedShow)
            return true;

        string toCompare = property.name;
        if (isPath)
            return property.FindPropertyRelative(property.propertyPath) != null;

        var searchPattern = SearchPatternToUse;

        // only use to lower on contains because contains do not allow us to pas OrdinalIgnoreCase
        if (searchPattern == SearchPattern.Contains)
            toCompare = toCompare.ToLower();

        searchAsLow = searchAsLow.Trim();

        if (toCompare.StartsWith("m_"))
            toCompare = toCompare.Remove(0, 2);
        else if (toCompare.StartsWith("_"))
            toCompare = toCompare.Remove(0, 1);

        string[] parts = new[] { searchAsLow };
        if (searchAsLow.Contains(' '))
        {
            parts = searchAsLow.Split(' ');
        }

        bool contains = true;
        for (int i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            if (string.IsNullOrEmpty(part))
                contains = false;
            else
            {
                switch (SearchPatternToUse)
                {
                    case SearchPattern.StartsWith:
                        contains &= toCompare.StartsWith(part, StringComparison.OrdinalIgnoreCase);
                        break;
                    case SearchPattern.EndsWith:
                        contains &= toCompare.EndsWith(part, StringComparison.OrdinalIgnoreCase);
                        break;
                    case SearchPattern.Match:
                        contains &= toCompare.Equals(part, StringComparison.OrdinalIgnoreCase);
                        break;
                    case SearchPattern.Type:
                        {
                            string typeName = property.type;
                            switch (property.propertyType)
                            {
                                case SerializedPropertyType.ObjectReference:
                                    if (property.objectReferenceValue != null)
                                        typeName = property.objectReferenceValue.GetType().Name;
                                    break;
                            }

                            contains &= typeName.Equals(part, StringComparison.OrdinalIgnoreCase);
                        }
                        break;
                    case SearchPattern.Contains:
                        contains &= toCompare.Contains(part);
                        break;
                    case SearchPattern.Value:
                        contains &= CompareValue(property, part);
                        break;
                }
            }
        }

        return contains;
    }
    /// <summary>
    /// Compared the property's value to toCompare parameter.
    /// Returns true if the property's value constains toCompare.
    /// </summary>
    private bool CompareValue(SerializedProperty property, string toCompare)
    {
        string stringValue = string.Empty;
        switch (property.propertyType)
        {
            // Parse the types we know how to parse
            case SerializedPropertyType.Integer:
                int toCompareInt;
                if (int.TryParse(toCompare, out toCompareInt))
                {
                    if (property.intValue == toCompareInt)
                        return true;
                }
                break;
            case SerializedPropertyType.Boolean:
                bool toCompareBool;
                if (bool.TryParse(toCompare, out toCompareBool))
                    return toCompareBool == property.boolValue;
                break;
            case SerializedPropertyType.Float:
                float toCompareFloat;
                if (float.TryParse(toCompare, out toCompareFloat))
                {
                    if (Mathf.Abs(property.floatValue - toCompareFloat) < float.Epsilon)
                        return true;
                }
                break;

            // Types that doesn't have explicit values
            case SerializedPropertyType.Generic:
            case SerializedPropertyType.LayerMask:
            case SerializedPropertyType.Enum:
            case SerializedPropertyType.Character:
            case SerializedPropertyType.Gradient:
            case SerializedPropertyType.Quaternion:
                return false;

            // Types that have explicit values
            case SerializedPropertyType.String:
                stringValue = property.stringValue;
                break;
            case SerializedPropertyType.Color:
                stringValue = property.colorValue.ToString();
                break;
            case SerializedPropertyType.ObjectReference:
                if (property.objectReferenceValue != null)
                    stringValue = property.objectReferenceValue.ToString();
                break;
            case SerializedPropertyType.Vector2:
                stringValue = property.vector2Value.ToString();
                break;
            case SerializedPropertyType.Vector3:
                stringValue = property.vector3Value.ToString();
                break;
            case SerializedPropertyType.Vector4:
                stringValue = property.vector4Value.ToString();
                break;
            case SerializedPropertyType.Rect:
                stringValue = property.rectValue.ToString();
                break;
            case SerializedPropertyType.ArraySize:
                if (property.isArray)
                    stringValue = property.arraySize.ToString();
                break;
            case SerializedPropertyType.AnimationCurve:
                stringValue = property.animationCurveValue.ToString();
                break;
            case SerializedPropertyType.Bounds:
                stringValue = property.boundsValue.ToString();
                break;
        }

        return stringValue.ToLower().Trim().Contains(toCompare);
    }

    #endregion

    #region Helper

    /// <summary>
    /// Get the callback of highlight object button
    /// </summary>
    /// <param name="property"></param>
    /// <returns></returns>
    private Action GetObjectToHight(DrawableProperty property)
    {
        var isMultiple = !(property.UnityObjects == null || property.UnityObjects.Count == 0);

        //if (isMultiple && property.UnityObjects.Count > 1)
        //   return null;

        Object toHighlight = null;
        if (isMultiple)
            toHighlight = property.Object.targetObjects[0];
        else
            toHighlight = property.Object.targetObject;

        Component comp = toHighlight as Component;
        if (comp != null)
            toHighlight = comp.gameObject;

        return () =>
        {
            Selection.objects = property.Object.targetObjects;
        };//(toHighlight);
    }

    /// <summary>
    /// Get the callback for openning a script
    /// </summary>
    private Action GetOpenScriptCallback(DrawableProperty property)
    {
        var scriptRef = property.Object.FindProperty("m_Script");
        if (scriptRef == null)
            return null;

        return () =>
        {
            AssetDatabase.OpenAsset(scriptRef.objectReferenceValue);
        };
    }

    /// <summary>
    /// Update the state of all serializedObjects
    /// If any of them has been destroyed, refresh selection and filter
    /// </summary>
    private void UpdateAllProperties()
    {
        for (int i = _drawable.Count - 1; i >= 0; i--)
        {
            if (_drawable[i].HasDestroyedObject())
            {
                FilterSelected();
                break;
            }
            UpdateProperties(_drawable[i]);
        }
    }

    /// <summary>
    /// Update the serialized object of the drawable property
    /// </summary>
    private void UpdateProperties(DrawableProperty drawable)
    {
        drawable.Object.Update();
        for (int i = 0; i < drawable.Childs.Count; i++)
        {
            UpdateProperties(drawable.Childs[i]);
        }
    }

    /// <summary>
    /// Update the last drawn rect - position of the last contorl
    /// </summary>
    private void UpdateLastRectDraw()
    {
        if (Event.current.type == EventType.repaint)
        {
            _lastDrawPosition = GUILayoutUtility.GetLastRect();
        }
    }

    /// <summary>
    /// Returns true if we should stop drawing
    /// </summary>
    /// <returns></returns>
    private bool ShouldSkipDrawing()
    {
        // update the last rect drawn
        // this is used to validated if we are drawing outside of screen
        UpdateLastRectDraw();

        if (Event.current.type == EventType.repaint)
        {
            var pos = position;
            pos.position = _scrollPosition;
            return _lastDrawPosition.yMax >= pos.yMax;
        }

        return false;
    }

    /// <summary>
    /// Create editor for all drawbales
    /// </summary>
    private void CreateEditorForAll()
    {
        for (int i = 0; i < _drawable.Count; i++)
        {
            CreateEditorForProperty(_drawable[i]);
        }
    }

    /// <summary>
    /// Create a editor for just one drawable and its children
    /// </summary>
    /// <param name="property"></param>
    private void CreateEditorForProperty(DrawableProperty property)
    {
        property.CustomEditor = Editor.CreateEditor(property.UnityObjects.ToArray());
        for (int i = 0; i < property.Childs.Count; i++)
        {
            CreateEditorForProperty(property.Childs[i]);
        }
    }

    private bool GetState(DrawableProperty property, bool defaultValue)
    {
        return GetState(property.Id, defaultValue);
    }

    private bool GetState(int id, bool defaultValue)
    {
        bool state;
        if (_headersState.TryGetValue(id, out state))
            return state;
        return defaultValue;
    }

    private void SetState(DrawableProperty property, bool value)
    {
        _headersState[property.Id] = value;
    }

    private void SetStateInChild(DrawableProperty property, bool value)
    {
        for (int i = 0; i < property.Childs.Count; i++)
        {
            SetState(property.Childs[i], value);
        }
        _headersState[property.ButtonsId] = value;
    }

    #endregion

    #region UI Helpers

    /// <summary>
    /// Open progress bar
    /// </summary>
    private bool HandleProgressBar(float progress)
    {
        if (EditorApplication.timeSinceStartup - _startSearchTime > 2)
            EditorUtility.DisplayProgressBar("Searching", "Please wait", progress);

        if (EditorApplication.timeSinceStartup - _startSearchTime > 10)
            return true;

        return false;
    }

    /// <summary>
    /// Start a area with distinctive background
    /// This function is based on NGUI's BeginContents with minor changes
    /// </summary>
    static public void BeginContents()
    {
        GUILayout.BeginHorizontal();
        EditorGUILayout.BeginHorizontal("AS TextArea", GUILayout.MinHeight(10f));
        GUILayout.BeginVertical();

        GUILayout.Space(2f);
    }

    /// <summary>
    /// Start a area with distinctive background
    /// This function is based on NGUI's EndContents with minor changes
    /// </summary>
    static public void EndContents()
    {
        GUILayout.Space(3f);

        GUILayout.EndVertical();
        EditorGUILayout.EndHorizontal();

        GUILayout.Space(3f);
        GUILayout.EndHorizontal();

        GUILayout.Space(3f);
    }

    /// <summary>
    /// Draw a header with a name and possibly three buttons.
    /// If any of these buttons callback are null, the correspondent button will be disabled
    /// This function is also based on NGUI's DrawHeader.
    /// </summary>
    private bool DrawHeader(string text, DrawableProperty prop, Action onButtonClick = null, Action onApplyCallback = null,
        Action onRevertCallback = null, Action openScriptAction = null, Action collapseChilds = null, Action expandChilds = null)
    {
        var state = GetState(prop, true);

        GUILayout.Space(3f);
        if (!state)
            GUI.backgroundColor = new Color(0.8f, 0.8f, 0.8f);

        GUILayout.BeginHorizontal();
        GUI.changed = false;

        var evType = Event.current.type;
        if (DrawHeader(text, state))
        {
            if (evType == EventType.MouseUp && Event.current.control)
            {
                OpenContextMenu(prop);
            }
            else
            {
                state = !state;
            }
        }

        #region Applyu/Revert

        if (onApplyCallback == null)
            GUI.enabled = false;
        if (!GUILayout.Toggle(true, new GUIContent("Apply", tooltip: GUI.enabled ? "Apply changes to prefab" : "There's no changes to apply"), "dragtab", GUILayout.Width(50)))
        {
            if (onApplyCallback != null)
                onApplyCallback();
        }
        GUI.enabled = true;

        if (onRevertCallback == null)
            GUI.enabled = false;
        if (!GUILayout.Toggle(true, new GUIContent("Revert", tooltip: GUI.enabled ? "Revert changes" : "There's no changes to revert"), "dragtab", GUILayout.Width(60)))
        {
            if (onRevertCallback != null)
                onRevertCallback();
        }
        GUI.enabled = true;

        #endregion

        #region Expand/Collapse

        if (expandChilds == null)
            GUI.enabled = false;
        if (!GUILayout.Toggle(true, _expandGUIContent, "dragtab", GUILayout.Width(25)))
        {
            if (expandChilds != null)
                expandChilds();
        }
        GUI.enabled = true;

        if (collapseChilds == null)
            GUI.enabled = false;
        if (!GUILayout.Toggle(true, _collapseGUIContent, "dragtab", GUILayout.Width(25)))
        {
            if (collapseChilds != null)
                collapseChilds();
        }
        GUI.enabled = true;

        #endregion

        GUILayout.Space(2);

        #region Edit script

        if (openScriptAction == null)
        {
            GUI.enabled = false;
        }
        if (!GUILayout.Toggle(true, _openScriptContent, "dragtab", GUILayout.Width(25)))
        {
            if (openScriptAction != null)
                openScriptAction();
        }
        GUI.enabled = true;

        #endregion

        GUILayout.Space(2);

        #region Highlight object

        var toolTip = "Select object";
        if (onButtonClick == null)
        {
            //GUI.enabled = false;
            toolTip = "Select multiple objects";
        }
        _highlightGUIContent.tooltip = toolTip;
        if (!GUILayout.Toggle(true, _highlightGUIContent, "dragtab", GUILayout.Width(25)))
        {
            if (onButtonClick != null)
                onButtonClick();
        }
        GUI.enabled = true;

        #endregion

        if (GUI.changed)
            SetState(prop, state);

        GUILayout.Space(2f);
        GUILayout.EndHorizontal();
        GUI.backgroundColor = Color.white;
        if (!state)
            GUILayout.Space(3f);
        return state;
    }

    private bool DrawHeader(string text, bool state)
    {
        text = "<b><size=11>" + text + "</size></b>";
        if (state)
            text = "\u25BC " + text;
        else
            text = "\u25BA " + text;
        return !GUILayout.Toggle(true, text, "dragtab", GUILayout.MinWidth(20f), GUILayout.ExpandWidth(true));
    }

    /// <summary>
    /// Add lock option to menu (that where you choose to close a tab)
    /// </summary>
    /// <param name="menu"></param>
    public void AddItemsToMenu(GenericMenu menu)
    {
        menu.AddItem(new GUIContent("Lock"), _locked, () =>
        {
            _locked = !_locked;
            _lockedObjects = Selection.objects;
            FilterSelected();
        });
        menu.AddItem(new GUIContent("New property Inspector"), false, InitWindow);
    }

    private void ExpandAll()
    {
        for (int i = 0; i < _drawable.Count; i++)
        {
            SetState(_drawable[i], true);
            //_headersState[_drawable[i].ButtonsId] = true;
        }
        _expandAll = false;
    }

    private void CollapseAll()
    {
        for (int i = 0; i < _drawable.Count; i++)
        {
            SetState(_drawable[i], false);
            //_headersState[_drawable[i].ButtonsId] = false;
        }
        _collapseAll = false;
    }

    private void OpenContextMenu(DrawableProperty property)
    {
        var methods = GetMethods(property);
        GenericMenu menu = new GenericMenu();
        for (int i = 0; i < methods.Count; i++)
        {
            var method = methods[i];
            menu.AddItem(new GUIContent(method.Name), false, (data) => CallMethod(data as MethodInfo, _rightClickedObjects), method);
        }

        menu.ShowAsContext();
    }

    private void DrawMethodButtons(DrawableProperty property)
    {
        if (_showMethodAsButtons)
        {
            var state = GetState(property.ButtonsId, false);
            if (!state)
                GUI.backgroundColor = new Color(0.8f, 0.8f, 0.8f);
            GUILayout.Space(5f);
            if (DrawHeader("Methods", state))
            {
                state = !state;
                _headersState[property.ButtonsId] = state;
            }

            if (state)
            {
                BeginContents();
                {
                    var methods = GetMethods(property);
                    for (int i = 0; i < methods.Count; i++)
                    {
                        var method = methods[i];
                        if (GUILayout.Button(method.Name))
                        {
                            CallMethod(method, property.UnityObjects);
                        }
                    }
                }
                EndContents();
            }
            GUI.backgroundColor = Color.white;
        }
    }

    #endregion

    #region Help

    /// <summary>
    /// Show the help message box.
    /// </summary>
    public void ShowHelp()
    {
        var title = ("About Property Inspector v." + Version);
        var message = @"Use the search bar to filter a property.

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

        EditorUtility.DisplayDialog(title, message, "OK");
    }

    #endregion

    #region Utils

    private List<MethodInfo> GetMethods(DrawableProperty prop)
    {
        var objects = prop.Object.targetObjects;
        _rightClickedObjects = objects.ToList();
        var type = objects[0].GetType();
        var methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).ToList();
        methods = methods.FindAll(m => !m.ContainsGenericParameters && !m.IsGenericMethod && !m.IsSpecialName && m.GetParameters().Length == 0);
        methods.Sort((m1, m2) => string.Compare(m1.Name, m2.Name));
        return methods;
    }

    private void CallMethod(MethodInfo method, List<Object> objects)
    {
        if (method.IsStatic)
        {
            method.Invoke(null, null);
        }
        else
        {
            for (int i = 0; i < objects.Count; i++)
            {
                var value = method.Invoke(objects[i], null);
                if (value != null)
                    Debug.Log(string.Format("Return value for call to method {0} of object {1}: {2}", method.Name, objects[i], value));
            }
        }
    }

    /// <summary>
    /// Update objects that are locked.
    /// This is necessary because when lock mode is on
    /// a object can be destroy while we are still holding it,
    /// so we remove from our list objects that have been destroyed.
    /// </summary>
    private void UpdateLockedObject()
    {
        if (_lockedObjects == null)
            _lockedObjects = new Object[0];

        var objects = _lockedObjects.ToList();
        for (int i = objects.Count - 1; i >= 0; i--)
        {
            if (objects[i] == null || !objects[i])
                objects.RemoveAt(i);
        }

        _lockedObjects = objects.ToArray();
    }

    #endregion
}
