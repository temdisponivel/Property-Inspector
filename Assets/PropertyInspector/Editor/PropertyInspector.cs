﻿using System;
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

        public override int GetHashCode()
        {
            // return a unique (or almost anyway) integer based on the Unity Objects that we are editing
            int hashCode = base.GetHashCode();
            if (!Object.isEditingMultipleObjects)
                return Object.targetObject.GetInstanceID();
            else
            {
                for (int i = 0; i < Object.targetObjects.Length; i++)
                {
                    var currentObject = Object.targetObjects[i];
                    hashCode += currentObject.GetInstanceID();
                }
            }
            return hashCode;
        }
    }

    #endregion

    private readonly List<DrawableProperty> _drawable = new List<DrawableProperty>();

    private const string SearchFieldName = "SearchQuery";
    private const string ShowHiddenKey = "SUSHOWHIDDEN";
    private const string InspectorModeKey = "SUINSPECTORMODE";
    private const string MultipleEditKey = "MultipleEditKey";

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
    private bool _showHidden;
    private bool _applyAll;
    private bool _revertAll;

    private readonly Version Version = new Version(1, 0, 0, 0);

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
            if (_currentSearchedQuery.StartsWith("s:", StringComparison.OrdinalIgnoreCase))
                return SearchPattern.StartsWith;
            else if (_currentSearchedQuery.StartsWith("e:", StringComparison.OrdinalIgnoreCase))
                return SearchPattern.EndsWith;
            else if (_currentSearchedQuery.StartsWith("m:", StringComparison.OrdinalIgnoreCase))
                return SearchPattern.Match;
            else if (_currentSearchedQuery.StartsWith("t:", StringComparison.OrdinalIgnoreCase))
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

                _titleGUIContentCache = new GUIContent("Property Inspector", EditorGUIUtility.Load(textToLoad) as Texture2D);
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
        window.titleContent = _titleGUIContent;
        window._focus = true;
        window.wantsMouseMove = true;
        window.autoRepaintOnSceneChange = true;
        window.minSize = new Vector2(400, window.minSize.y);

        window._showHidden = EditorPrefs.GetBool(ShowHiddenKey + window._openedAsUtility, false);
        window._inspectorMode = EditorPrefs.GetBool(InspectorModeKey + window._openedAsUtility, false);
        window._multipleEdit = EditorPrefs.GetBool(MultipleEditKey + window._openedAsUtility, false);

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

        var edit = EditorGUILayout.ToggleLeft(new GUIContent("Mult-edit", tooltip: "Edit multiple objects as one"), _multipleEdit, GUILayout.MaxWidth(80));
        var showHidden = EditorGUILayout.ToggleLeft("Show hidden", _showHidden, GUILayout.MaxWidth(100));
        var inspectorMode = EditorGUILayout.ToggleLeft(new GUIContent("Inspector mode", tooltip: "Show all properties when search query is empty (slow when viewing numerous objects without query)"), _inspectorMode, GUILayout.MaxWidth(150));

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

        if (showHidden != _showHidden)
        {
            filter = true;
            EditorPrefs.SetBool(ShowHiddenKey + _openedAsUtility, showHidden);
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

        if (_openedAsUtility)
        {
            if (Event.current != null)
            {
                if (Event.current.type == EventType.keyDown && Event.current.keyCode == KeyCode.Escape)
                {
                    Close();
                    return;
                }

                if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.F && Event.current.modifiers == EventModifiers.Control)
                {
                    Focus();
                }
            }
        }

        if (_lastSearchedQuery != _currentSearchedQuery)
        {
            if (EditorApplication.timeSinceStartup >= _timeToSearchAgain)
            {
                FilterSelected();
                _lastSearchedQuery = _currentSearchedQuery;
                _timeToSearchAgain = EditorApplication.timeSinceStartup + .3f;
            }
        }

        //Repaint();
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

        // this is being called from inside UpdateAllProperties
        //if (_expandAll)
        //    ExpandAll();
        //else if (_collapseAll)
        //    CollapseAll();

        // Iterate through all drawables and draw them into the screen
        // This drawable have already been filtereds and are ready to draw
        for (int i = 0; i < _drawable.Count; i++)
        {
            var current = _drawable[i];

            // if it's editing multiple objects, change name
            var name = string.Format("{0} (Multiple ({1}))", current.Type, current.Object.targetObjects.Length);
            bool isMultiple = !(current.UnityObjects == null || current.UnityObjects.Count == 0);
            if (!isMultiple)
                name = current.UnityObject.name;

            // get callbacks for tab buttons
            // this are the callbacks called when user clicks in "Apply", "Revert" or "Highlight" on object header
            Action buttonCallback = GetObjectToHight(current);
            Action applyCallback = null;
            Action revertCallback = null;

            // If there's changes to be applied to prefabs, enable buttons of apply and revert
            if (current.HasAppliableChanges)
            {
                applyCallback = () => ApplyChangesToPrefab(current);
                revertCallback = () => RevertChangesToPrefab(current);
            }

            // Draw a header if the object name and pass the callback
            // If one of these callbacks is null, the header will disable the button that would trigger it
            if (DrawHeader(name, current.GetHashCode().ToString(), buttonCallback, applyCallback, revertCallback))
            {
                BeginContents();

                EditorGUI.BeginChangeCheck();

                // draw the actual property into the screen
                var serializedObject = current.Object;
                foreach (var serializedProperty in current.Properties)
                {
                    EditorGUILayout.PropertyField(serializedProperty, true);
                }

                // if the player has altered the object between BeginChangeCheck and this point,
                // the below call will return true and we should apply those changes
                if (EditorGUI.EndChangeCheck())
                    serializedObject.ApplyModifiedProperties();

                // if these object has childs (components are child of game objects in single mode (not multi-edit mode))
                if (current.Childs.Count > 0)
                {
                    // do basically the same we just did but for every child of this object
                    for (int j = 0; j < current.Childs.Count; j++)
                    {
                        var currentChild = current.Childs[j];

                        name = string.Format("{0} (Multiple ({1}))", currentChild.Type, currentChild.Object.targetObjects.Length);

                        isMultiple = !(currentChild.UnityObjects == null || currentChild.UnityObjects.Count == 0);
                        if (!isMultiple)
                            name = currentChild.UnityObject.GetType().Name;

                        buttonCallback = GetObjectToHight(currentChild);
                        applyCallback = null;
                        revertCallback = null;

                        if (currentChild.HasAppliableChanges)
                        {
                            applyCallback = () => ApplyChangesToPrefab(currentChild);
                            revertCallback = () => RevertChangesToPrefab(currentChild);
                        }

                        if (DrawHeader(name, currentChild.GetHashCode().ToString(), buttonCallback, applyCallback, revertCallback))
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

                            // update the last rect drawn
                            // this is used to validated if we are drawing outside of screen
                            UpdateLastRectDraw();

                            // if we are drawing outside of screen, stop drawing - because there's no point in that
                            if (ShouldSkipDrawing())
                                break;
                        }
                    }
                }

                EndContents();

                UpdateLastRectDraw();

                if (ShouldSkipDrawing())
                    break;
            }
        }

        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }

    #endregion

    #region Filter

    /// <summary>
    /// Entry point for filtering.
    /// </summary>
    private void FilterSelected()
    {
        _drawable.Clear();

        if (_multipleEdit)
            FilterMultiple();
        else
            FilterSingles();

        ValidaIfCanApplyAll();
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
                    if (components[j].GetType() == typeof(Transform))
                        continue;

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

            if (string.IsNullOrEmpty(_currentSearchedQuery) && !_inspectorMode)
            {
                _drawable.Add(drawable);
                continue;
            }
            else
            {
                FilterProperties(null, drawable, serializedObject, iterator, searchAsLow, isPath);
            }

            // Add all objects and properties to the list of drawables 
            // this list is used to cache all properties and objects that will be drawn
            // we will go through this list later and contruct our actual drawable properties
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
                currentDrawableProperty.Properties.Add(currentDrawableProperty.Object.FindProperty(propertiesPath));
            }

            _drawable.Add(currentDrawableProperty);
        }

        _searching = false;

        EditorUtility.ClearProgressBar();
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

        var drawableChild = new DrawableProperty()
        {
            UnityObject = uObject,
            Object = childSerializedObject,
        };

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
        bool stepInto = true;

        // Get the next property on this level (never go deeper inside a property)
        while (iterator.Next(stepInto))
        {
            stepInto = false;
            // if it's a non editable and we should NOT show hidden, go to next property
            if (!_showHidden && !iterator.editable)
            {
                continue;
            }

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
                child.Properties.Add(property);
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

                child.Properties.Add(property);
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

    /// <summary>
    /// Apply all changes made on that drawable property to the prefabs
    /// connected to the objects inside the drawable property
    /// </summary>
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
            toCompare = property.propertyPath;

        var searchPattern = SearchPatternToUse;

        // only use to lower on contains because contains do not allow us to pas OrdinalIgnoreCase
        if (searchPattern == SearchPattern.Contains)
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
                    contains &= toCompare.StartsWith(parts[i], StringComparison.OrdinalIgnoreCase);
                    break;
                case SearchPattern.EndsWith:
                    contains &= toCompare.EndsWith(parts[i], StringComparison.OrdinalIgnoreCase);
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

        if (_expandAll)
            EditorPrefs.SetBool(drawable.GetHashCode().ToString(), true);
        else if (_collapseAll)
            EditorPrefs.SetBool(drawable.GetHashCode().ToString(), false);
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
        if (Event.current.type == EventType.repaint)
        {
            var pos = position;
            pos.position = _scrollPosition;
            return _lastDrawPosition.yMax >= pos.yMax;
        }

        return false;
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
    static public bool DrawHeader(string text, string key, Action onButtonClick = null, Action onApplyCallback = null, Action onRevertCallback = null)
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

        var toolTip = "Highlight object";
        if (onButtonClick == null)
        {
            GUI.enabled = false;
            toolTip = "Can't highlight multiple objects";
        }
        _highlightGUIContent.tooltip = toolTip;
        if (!GUILayout.Toggle(true, _highlightGUIContent, "dragtab", GUILayout.Width(35)))
        {
            if (onButtonClick != null)
                onButtonClick();
        }
        GUI.enabled = true;

        if (GUI.changed)
            EditorPrefs.SetBool(key, state);

        GUILayout.Space(2f);
        GUILayout.EndHorizontal();
        GUI.backgroundColor = Color.white;
        if (!state)
            GUILayout.Space(3f);
        return state;
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
    }

    private void ExpandAll()
    {
        for (int i = 0; i < _drawable.Count; i++)
        {
            SetEditorPrefsForObject(_drawable[i], true);
        }
        _expandAll = false;
    }

    private void CollapseAll()
    {
        for (int i = 0; i < _drawable.Count; i++)
        {
            SetEditorPrefsForObject(_drawable[i], false);
        }
        _collapseAll = false;
    }

    private void SetEditorPrefsForObject(DrawableProperty property, bool value)
    {
        EditorPrefs.SetBool(property.GetHashCode().ToString(), value);
        for (int i = 0; i < property.Childs.Count; i++)
        {
            SetEditorPrefsForObject(property.Childs[i], value);
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
                        You can use the prefixed: “s:”, “e:”, “t:”.
                        Where:
                        “s:”: Starts with - will show only properties whose names starts with the text typed.
                        “e:”: Ends with - will show only properties whose names ends with the text typed.
                        “t:”: Type - will show only properties whose type match the text typed.

                        None of those options are case sensitive.

                        You can search using the path of the property you want to see.For example: Player.HealthHandler.Life would only show the property Life of the property HealthHandler of the property Player.  This options IS case sensitive.

                        Multi-edit group objects and components by type and lets you edit multiple objects as if they were one. All changes made on this mode affect all object in the group.

                        Show hidden shows non editable properties - most common in scriptable objects.

                        Inspector mode will show all properties of all object when there’s no search typed.

                        Apply all/Revert all will apply or revert all changes made in objects that are instances of prefabs.

                        Apply/Revert buttons in headers will apply or revert changes made in that object.

                        Highlight button highlights the objects in the hierarchy or project.

                        If you have any question, ran into bug or problem or have a suggestion please don’t hesitate in contating me at: temdisponivel@gmail.com.

                        For more info, please see the pdf file inside PropertyInspector’s folder or visit: http://goo.gl/kyX3A3
                        ";

        EditorUtility.DisplayDialog(title, message, "OK");
    }

    #endregion
}
