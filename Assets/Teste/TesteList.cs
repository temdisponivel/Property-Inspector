using System;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Assets.Teste;

[Serializable]
public class TesteList
{
    public string Name;
    public int Age;

    public TestBehaviour[] Objects;

    public List<TestClass> Teste;
}