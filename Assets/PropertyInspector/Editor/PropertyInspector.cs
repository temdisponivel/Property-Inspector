using System;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using Object = UnityEngine.Object;

[ExecuteInEditMode]
public class PropertyInspector : EditorWindow, IHasCustomMenu
{
    #region Inner type

    private enum SearchPattern
    {
        StartsWith,
        EndsWith,
        Contains,
        Match,
        Type,
    }

    private class DrawableProperty
    {
        public DrawableProperty()
        {
            UnityObjects = new List<Object>();
            Childs = new List<DrawableProperty>();
            Properties = new List<SerializedProperty>();
            PropertiesPaths = new HashSet<string>();
        }

        public Object UnityObject { get; set; }
        public List<Object> UnityObjects { get; set; }
        public Type Type { get; set; }
        public SerializedObject Object { get; set; }
        public List<SerializedProperty> Properties { get; set; }
        public HashSet<string> PropertiesPaths { get; set; }
        public List<DrawableProperty> Childs { get; set; }
        public bool HasAppliableChanges { get; set; }
    }

    #endregion

    private const string SearchFieldName = "SearchQuery";

    private const string ShowHiddenKey = "SUSHOWHIDDEN";
    private const string InspectorModeKey = "SUINSPECTORMODE";
    private const string MultipleEditKey = "MultipleEditKey";

    public bool _openedAdUtility { get; set; }

    private readonly List<DrawableProperty> _drawable = new List<DrawableProperty>();

    private Vector2 _scrollPosition = Vector2.zero;

    private bool _focus = false;
    private string _lastSearchedQuery = string.Empty;
    private string _currentSearchedQuery = string.Empty;
    private double _startSearchTime;
    public double _timeToSearchAgain;
    private bool _searching;
    private bool _showAll;
    private bool _collapseAll;

    private bool _multipleEdit;
    private bool _locked;
    private bool _inspectorMode;
    private bool _showHidden;

    private bool _applyAll;

    private const string Version = "0.0.0.1";

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

    private SearchPattern SearchPatternToUse
    {
        get
        {
            if (_currentSearchedQuery.StartsWith("s:"))
                return SearchPattern.StartsWith;
            else if (_currentSearchedQuery.StartsWith("e:"))
                return SearchPattern.EndsWith;
            else if (_currentSearchedQuery.StartsWith("m:"))
                return SearchPattern.Match;
            else if (_currentSearchedQuery.StartsWith("t:"))
                return SearchPattern.Type;
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

                _highlightGuiContentCache = new GUIContent(EditorGUIUtility.Load(textToLoad) as Texture2D, "Highlight object");
            }
            return _highlightGuiContentCache;
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
                
                _titleGUIContentCache = new GUIContent("Property Inspector", EditorGUIUtility.Load(textToLoad) as Texture2D, "Show all properties when search is empty");
            }

            return _titleGUIContentCache;
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
                _collapseGUIContentCache = new GUIContent(EditorGUIUtility.Load(textToLoad) as Texture2D, tooltip: "Collapse all");
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
                __expandGUIContentCache = new GUIContent(EditorGUIUtility.Load(textToLoad) as Texture2D, tooltip: "Expand all");
            }

            return __expandGUIContentCache;
        }
    }

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

    #region Init

    [MenuItem("Window/Property Inspector Popup &f")]
    private static void Init()
    {
        var window = CreateInstance<PropertyInspector>();
        window._openedAdUtility = true;
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

    static void SetupInfo(PropertyInspector window)
    {
        window.titleContent = _titleGUIContentCache;
        window._focus = true;
        window.wantsMouseMove = true;
        window.autoRepaintOnSceneChange = true;
        window.minSize = new Vector2(400, window.minSize.y);

        window._showHidden = EditorPrefs.GetBool(ShowHiddenKey + window._openedAdUtility, false);
        window._inspectorMode = EditorPrefs.GetBool(InspectorModeKey + window._openedAdUtility, false);
        window._multipleEdit = EditorPrefs.GetBool(MultipleEditKey + window._openedAdUtility, false);

        window.FilterSelected();
    }

    void OnSelectionChange()
    {
        if (!_locked)
            FilterSelected();
        Repaint();
    }

    void OnInspectorUpdate()
    {
        Repaint();
    }

    void OnFocus()
    {
        //FilterSelected();
        Repaint();
        _focus = true;
    }

    #endregion

    #region Title

    private void DrawSearchField()
    {
        bool filter = false;

        GUI.SetNextControlName(SearchFieldName);

        GUILayout.BeginVertical("In BigTitle");
        EditorGUILayout.BeginVertical();
        GUILayout.Space(5);
        EditorGUILayout.BeginHorizontal();

        EditorGUILayout.LabelField(_titleGUIContent, "LargeLabel");

        GUILayout.FlexibleSpace();

        if (_openedAdUtility)
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

        _currentSearchedQuery = EditorGUILayout.TextField(_currentSearchedQuery, (GUIStyle)"ToolbarSeachTextField", GUILayout.Width(position.width - 25));

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

        var edit = EditorGUILayout.ToggleLeft(new GUIContent("Multiple edit", tooltip: "Edit multiple objects as one"), _multipleEdit, GUILayout.MaxWidth(100));
        var showHidden = EditorGUILayout.ToggleLeft("Show hidden", _showHidden, GUILayout.MaxWidth(100));
        var inspectorMode = EditorGUILayout.ToggleLeft(new GUIContent("Inspector mode", tooltip: "Show all properties when search query is empty (slow when viewing numerous objects without query)"), _inspectorMode, GUILayout.MaxWidth(150));

        GUILayout.FlexibleSpace();

        _applyAll = GUILayout.Button(new GUIContent("Apply all", tooltip: "Apply all instance changes to prefabs"));

        _showAll = GUILayout.Button(_expandGUIContent, "miniButtonLeft");
        _collapseAll = GUILayout.Button(_collapseGUIContent, "miniButtonRight");

        EditorGUILayout.EndHorizontal();
        GUILayout.Space(5);
        EditorGUILayout.EndVertical();
        GUILayout.EndVertical();

        if (edit != _multipleEdit)
        {
            filter = true;
            EditorPrefs.SetBool(MultipleEditKey + _openedAdUtility, edit);
        }
        if (inspectorMode != _inspectorMode)
        {
            filter = true;
            EditorPrefs.SetBool(InspectorModeKey + _openedAdUtility, inspectorMode);
        }

        if (showHidden != _showHidden)
        {
            filter = true;
            EditorPrefs.SetBool(ShowHiddenKey + _openedAdUtility, showHidden);
        }

        _inspectorMode = inspectorMode;
        _showHidden = showHidden;
        _multipleEdit = edit;

        if (filter)
            FilterSelected();
    }

    #endregion

    #region Unity event

    void Update()
    {
        if (_searching)
            return;

        if (_openedAdUtility)
        {
            if (Event.current != null)
            {
                if (Event.current.type == EventType.keyDown && Event.current.keyCode == KeyCode.Escape)
                {
                    Close();
                    return;
                }
            }
        }

        if (_lastSearchedQuery != _currentSearchedQuery)
        {
            if (EditorApplication.timeSinceStartup > _timeToSearchAgain)
            {
                FilterSelected();
                _lastSearchedQuery = _currentSearchedQuery;
            }
            else
            {
                _timeToSearchAgain = EditorApplication.timeSinceStartup + .5f;
            }
        }

        Repaint();
    }

    private void OnGUI()
    {
        if (Event.current.type == EventType.Layout)
        {
            if (_applyAll)
            {
                ApplyAll();
                _applyAll = false;
            }
            ValidaIfCanApplyAll();
        }

        UpdateAllProperties();

        DrawSearchField();

        if (_focus)
        {
            EditorGUI.FocusTextInControl(SearchFieldName);
            _focus = false;
        }

        EditorGUILayout.BeginVertical();
        _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, GUILayout.Width(position.width), GUILayout.Height(position.height - 100));

        for (int i = 0; i < _drawable.Count; i++)
        {
            var current = _drawable[i];

            var name = string.Format("{0} (Multiples ({1}))", current.Type, current.Object.targetObjects.Length);
            bool isMultiple = !(current.UnityObjects == null || current.UnityObjects.Count == 0);
            if (!isMultiple)
                name = current.UnityObject.name;

            if (_showAll)
                EditorPrefs.SetBool(current.GetHashCode() + name, true);
            else if (_collapseAll)
                EditorPrefs.SetBool(current.GetHashCode() + name, false);

            Action buttonCallback = GetObjectToHight(current);
            Action applyCallback = null;

            if (current.HasAppliableChanges)
                applyCallback = () => ApplyChangesToPrefab(current);

            if (DrawHeader(name, current.GetHashCode() + name, buttonCallback, applyCallback))
            {
                BeginContents();

                EditorGUI.BeginChangeCheck();

                var serializedObject = current.Object;
                foreach (var serializedProperty in current.Properties)
                {
                    EditorGUILayout.PropertyField(serializedProperty, true);
                }

                if (EditorGUI.EndChangeCheck())
                    serializedObject.ApplyModifiedProperties();

                if (current.Childs.Count > 0)
                {
                    for (int j = 0; j < current.Childs.Count; j++)
                    {
                        var currentChild = current.Childs[j];

                        name = string.Format("{0} (Multiple ({1}))", currentChild.Type, currentChild.Object.targetObjects.Length);

                        isMultiple = !(currentChild.UnityObjects == null || currentChild.UnityObjects.Count == 0);
                        if (!isMultiple)
                            name = currentChild.UnityObject.GetType().Name;

                        if (_showAll)
                            EditorPrefs.SetBool(currentChild.GetHashCode() + name, true);
                        else if (_collapseAll)
                            EditorPrefs.SetBool(currentChild.GetHashCode() + name, false);

                        buttonCallback = GetObjectToHight(currentChild);
                        applyCallback = null;

                        if (currentChild.HasAppliableChanges)
                            applyCallback = () => ApplyChangesToPrefab(currentChild);

                        if (DrawHeader(name, currentChild.GetHashCode() + name, buttonCallback, applyCallback))
                        {
                            BeginContents();

                            EditorGUI.BeginChangeCheck();

                            var serializedObjectChild = currentChild.Object;

                            foreach (var serializedProperty in currentChild.Properties)
                            {
                                EditorGUILayout.PropertyField(serializedProperty, true);
                            }

                            if (EditorGUI.EndChangeCheck())
                                serializedObjectChild.ApplyModifiedProperties();

                            EndContents();
                        }
                    }
                }

                EndContents();
            }
        }

        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();

        _collapseAll = false;
        _showAll = false;
    }

    #endregion

    #region Filter

    private void FilterSelected()
    {
        _drawable.Clear();

        if (_multipleEdit)
            FilterMultiple();
        else
            FilterSingles();

        ValidaIfCanApplyAll();
    }


    private void FilterSingles()
    {
        var searchAsLow = _currentSearchedAsLower;
        _startSearchTime = EditorApplication.timeSinceStartup;
        _searching = true;
        bool isPath = _currentSearchedQuery.Contains('.');
        var objects = _objectsToFilter;

        for (int i = objects.Length - 1; i >= 0; i--)
        {
            var currentObject = objects[i];
            var serializedObject = new SerializedObject(currentObject);
            var iterator = serializedObject.GetIterator();

            var drawable = FilterObject(null, currentObject, searchAsLow, isPath, false);

            if (string.IsNullOrEmpty(_currentSearchedQuery) && !_inspectorMode)
            {
                _drawable.Add(drawable);
                continue;
            }
            else
            {
                FilterProperties(null, drawable, serializedObject, iterator, searchAsLow, isPath);
            }

            if (HandleProgressBar(i / objects.Length))
                break;

            var currentGameObject = currentObject as GameObject;
            if (currentGameObject != null)
            {
                var components = currentGameObject.GetComponents<Component>();

                for (int j = 0; j < components.Length; j++)
                {
                    if (components[j].GetType() == typeof(Transform))
                        continue;

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


    private void FilterMultiple()
    {
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

            if (string.IsNullOrEmpty(_currentSearchedQuery) && !_inspectorMode)
            {
                _drawable.Add(drawable);
                continue;
            }
            else
            {
                FilterProperties(null, drawable, serializedObject, iterator, searchAsLow, isPath);
            }

            AddObjectsAndProperties(drawables, drawable, currentObject);

            if (HandleProgressBar(i / objects.Length))
                break;

            var currentGameObject = currentObject as GameObject;
            if (currentGameObject != null)
            {
                var components = currentGameObject.GetComponents<Component>();

                for (int j = 0; j < components.Length; j++)
                {
                    if (components[j].GetType() == typeof(Transform))
                        continue;

                    var drawableChild = FilterObject(drawable, components[j], searchAsLow, isPath);
                    AddObjectsAndProperties(drawables, drawableChild, components[j]);
                }
            }
        }

        foreach (var drawableProperty in drawables)
        {
            DrawableProperty currentDrawableProperty = new DrawableProperty()
            {
                Object = new SerializedObject(drawableProperty.Value.UnityObjects.ToArray()),
                UnityObjects = drawableProperty.Value.UnityObjects,
                Type = drawableProperty.Key,
                PropertiesPaths = drawableProperty.Value.PropertiesPaths,
            };

            foreach (var propertiesPath in currentDrawableProperty.PropertiesPaths)
            {
                currentDrawableProperty.Properties.Add(currentDrawableProperty.Object.FindProperty(propertiesPath));
            }

            _drawable.Add(currentDrawableProperty);
        }

        _searching = false;

        EditorUtility.ClearProgressBar();
    }

    #endregion

    #region Filter properties

    private DrawableProperty FilterObject(DrawableProperty father, Object uObject, string search, bool isPath, bool filter = true)
    {
        var childSerializedObject = new SerializedObject(uObject);
        var childIterator = childSerializedObject.GetIterator();

        var drawableChild = new DrawableProperty()
        {
            UnityObject = uObject,
            Object = childSerializedObject,
        };

        if (filter)
            FilterProperties(father, drawableChild, childSerializedObject, childIterator, search, isPath);

        return drawableChild;
    }

    private void FilterProperties(DrawableProperty father, DrawableProperty child, SerializedObject serializedObject, SerializedProperty iterator, string search, bool isPath)
    {
        bool add = false;
        bool stepInto = true;
        while (iterator.Next(stepInto))
        {
            if (!_showHidden && !iterator.editable)
            {
                stepInto = false;
                continue;
            }

            stepInto = iterator.hasChildren && !iterator.isArray && iterator.propertyType == SerializedPropertyType.Generic;

            if (child.PropertiesPaths.Contains(iterator.propertyPath))
                continue;

            SerializedProperty property;
            if (Compare(iterator, search, isPath))
            {
                property = serializedObject.FindProperty(iterator.propertyPath);
                if (property == null)
                    continue;

                add = true;
                child.Properties.Add(property);
                child.PropertiesPaths.Add(property.propertyPath);
            }

            if (!isPath)
                continue;

            property = serializedObject.FindProperty(_currentSearchedQuery);
            if (property != null)
            {
                if (child.PropertiesPaths.Contains(property.propertyPath))
                    continue;

                child.Properties.Add(property);
                child.PropertiesPaths.Add(property.propertyPath);
                add = true;
            }
        }

        if (add && father != null)
            father.Childs.Add(child);
    }

    private void AddObjectsAndProperties(Dictionary<Type, DrawableProperty> drawables, DrawableProperty drawable, Object currentObject)
    {
        if (drawable.PropertiesPaths.Count == 0 && drawable.Childs.Count == 0)
            return;

        DrawableProperty drawableType;
        if (!drawables.TryGetValue(currentObject.GetType(), out drawableType))
            drawables[currentObject.GetType()] = (drawableType = new DrawableProperty());

        drawableType.UnityObjects.Add(currentObject);

        foreach (var propertiesPath in drawable.PropertiesPaths)
            drawableType.PropertiesPaths.Add(propertiesPath);
    }

    private void ApplyAll()
    {
        for (int i = 0; i < _drawable.Count; i++)
        {
            var current = _drawable[i];
            if (current.HasAppliableChanges)
                ApplyChangesToPrefab(current);
        }
    }

    private void ValidaIfCanApplyAll()
    {
        for (int i = 0; i < _drawable.Count; i++)
        {
            ValidateIfCanApply(_drawable[i]);
        }
    }

    bool ValidateIfCanApply(DrawableProperty property)
    {
        property.HasAppliableChanges = false;
        for (int i = 0; i < property.Properties.Count; i++)
        {
            var currentProperty = property.Properties[i];

            if (currentProperty.isInstantiatedPrefab && currentProperty.prefabOverride)
            {
                property.HasAppliableChanges = true;
                break;
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

    void ApplyChangesToPrefab(DrawableProperty property)
    {
        List<Object> objects = new List<Object>();

        objects.AddRange(property.UnityObjects);

        if (property.UnityObject != null)
            objects.Add(property.UnityObject);

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

            var prefab = PrefabUtility.GetPrefabParent(instance);
            if (prefab == null)
                continue;
            PrefabUtility.ReplacePrefab(instance, prefab, ReplacePrefabOptions.ConnectToPrefab);
        }

        property.HasAppliableChanges = false;
    }

    #endregion

    #region Compare

    public bool Compare(SerializedProperty property, string searchAsLow, bool isPath)
    {
        if (_forcedShow)
            return true;

        string toCompare = property.name;
        if (isPath)
            toCompare = property.propertyPath;

        toCompare = toCompare.ToLower();

        searchAsLow = searchAsLow.Trim();

        string[] parts = new[] { searchAsLow };
        if (searchAsLow.Contains(' '))
        {
            parts = searchAsLow.Split(' ');
        }

        bool contains = true;
        for (int i = 0; i < parts.Length; i++)
        {
            switch (SearchPatternToUse)
            {
                case SearchPattern.StartsWith:
                    contains &= toCompare.StartsWith(parts[i]);
                    break;
                case SearchPattern.EndsWith:
                    contains &= toCompare.EndsWith(parts[i]);
                    break;
                case SearchPattern.Match:
                    contains &= toCompare.Equals(parts[i], StringComparison.OrdinalIgnoreCase);
                    break;
                case SearchPattern.Type:
                    contains &= property.type.Equals(parts[i], StringComparison.OrdinalIgnoreCase);
                    break;
                case SearchPattern.Contains:
                    contains &= toCompare.Contains(parts[i]);
                    break;
            }
        }

        return contains;
    }

    private Action GetObjectToHight(DrawableProperty property)
    {
        var isMultiple = !(property.UnityObjects == null || property.UnityObjects.Count == 0);

        if (isMultiple && property.UnityObjects.Count > 1)
            return null;

        Object toHighlight = null;
        if (isMultiple)
            toHighlight = property.Object.targetObjects[0];
        else
            toHighlight = property.Object.targetObject;

        Component comp = toHighlight as Component;
        if (comp != null)
            toHighlight = comp.gameObject;

        return () => EditorGUIUtility.PingObject(toHighlight);
    }

    private void UpdateAllProperties()
    {
        for (int i = 0; i < _drawable.Count; i++)
        {
            UpdateProperties(_drawable[i]);
        }
    }

    private void UpdateProperties(DrawableProperty drawable)
    {
        drawable.Object.Update();
        for (int i = 0; i < drawable.Childs.Count; i++)
        {
            UpdateProperties(drawable.Childs[i]);
        }
    }

    #endregion

    #region UI

    private bool HandleProgressBar(float progress)
    {
        if (EditorApplication.timeSinceStartup - _startSearchTime > 2)
            EditorUtility.DisplayProgressBar("Searching", "Please wait", progress);

        if (EditorApplication.timeSinceStartup - _startSearchTime > 10)
            return true;

        return false;
    }

    static public void BeginContents()
    {
        GUILayout.BeginHorizontal();
        EditorGUILayout.BeginHorizontal("AS TextArea", GUILayout.MinHeight(10f));
        GUILayout.BeginVertical();

        GUILayout.Space(2f);
    }

    static public void EndContents()
    {
        GUILayout.Space(3f);

        GUILayout.EndVertical();
        EditorGUILayout.EndHorizontal();

        GUILayout.Space(3f);
        GUILayout.EndHorizontal();

        GUILayout.Space(3f);
    }

    static public bool DrawHeader(string text, string key, Action onButtonClick = null, Action onApplyCallback = null)
    {
        var state = EditorPrefs.GetBool(key, true);

        GUILayout.Space(3f);
        if (!state)
            GUI.backgroundColor = new Color(0.8f, 0.8f, 0.8f);

        GUILayout.BeginHorizontal();
        GUI.changed = false;

        text = "<b><size=11>" + text + "</size></b>";
        if (state)
            text = "\u25BC " + text;
        else
            text = "\u25BA " + text;

        if (!GUILayout.Toggle(true, text, "dragtab", GUILayout.MinWidth(20f), GUILayout.ExpandWidth(true)))
            state = !state;

        if (onApplyCallback != null)
        {
            if (!GUILayout.Toggle(true, new GUIContent("Apply"), "dragtab", GUILayout.Width(50)))
            {
                onApplyCallback();
            }
        }

        if (onButtonClick != null)
        {
            if (!GUILayout.Toggle(true, _highlightGUIContent, "dragtab", GUILayout.Width(35)))
            {
                onButtonClick();
            }
        }

        if (GUI.changed)
            EditorPrefs.SetBool(key, state);

        GUILayout.Space(2f);
        GUILayout.EndHorizontal();
        GUI.backgroundColor = Color.white;
        if (!state)
            GUILayout.Space(3f);
        return state;
    }

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

    public void AddItemsToMenu(GenericMenu menu)
    {
        menu.AddItem(new GUIContent("Lock"), _locked, () =>
        {
            _locked = !_locked;
            _lockedObjects = Selection.objects;
            FilterSelected();
        });
    }

    public void ShowHelp()
    {
        var title = ("About Property Inspector v." + Version);
        const string Bullet = "\u2022\u00A0";
        var message =
            Bullet + "Type something into the search box to filter properties inside selected objects/components.\n" +
            "All properties that contains the text typed (ignoring case) will be grouped by object and then by component inside that object. \n" +
            "\n" +
            "You can use prefix 's:' to show properties that start with the text typed;\n" +
            "Use prefix 'e:' to show properties that ends with the text typed;\n" +
            "Use prefix 'm:' to show properties that match the text typed;\n" +
            "Use prefix 't:' to show properties that have the same type as the text typed. Ex:\n" +
            "'t:Color' will show all Color properties of the selected object/components.\n" +
            Bullet + "You can type property paths to narrow your search. \n" +
            "Ex.: 'Materials.Array.data[0]' will show the first element (if it exists) of the array 'Materials'.\n" +
            "Note that in order for this to work, you have to type the exactly (case sensitive) path of the property you are looking for.\n" +
            Bullet + "Use collapse/expand buttons to collapse or expand all objects/components. \n" +
            Bullet + "When you check 'Edit mode', components will be groupped by type and act as one single component. " +
            "If you change a property of that component, all others (of the same type) will be changed as well.\n" +
            Bullet + "When 'Inspector mode' is on and there's no search typed, all properties of all objects/componenets will be shown. \n" +
            "Note that if you have numorous objects/components which have lots of properties, this feature can become a bit slow. " +
            "Since this is basically inspecting all those objects/components.\n" +
            Bullet + "Use the highlight button (hierarchy icon aside object/component headers) to hightlight the object in hierarchy or project. \n" +
            Bullet + "Use the padlock icon on the top to lock the current object selection. \n" +
            "\n" +
            "See read me file inside Search Utility's plugin folder for more info.\n";


        EditorUtility.DisplayDialog(title, message, "OK");
    }

    #endregion
}
