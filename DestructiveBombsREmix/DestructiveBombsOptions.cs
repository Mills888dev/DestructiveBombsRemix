using UnityEngine;
using System;
using System.Reflection;
using System.Reflection.Emit;

// This is only the part of the DestructiveBombs class that actually deals with ConfigMachine
public partial class DestructiveBombs : Partiality.Modloader.PartialityMod
{
    // The return type of OptionInterface works fine, but will break if a class inherits from OptionInterface
    public OptionalUI.OptionInterface LoadOI()
    {
        if (oiType == null)
            MakeOIType();
        
        return (OptionalUI.OptionInterface)Activator.CreateInstance(oiType, new object[] { this });
    }

    private Type oiType = null;
    private void MakeOIType()
    {
        // Create a class that contains a bare-bones structure - all it has to do is call functions from DBOProxy and return

        Debug.Log("Loading DestructiveBombsOptions...");
        // Create a new assembly that contains only the DestructiveBombsOptions type
        AssemblyName name = new AssemblyName("DesBombsOI");
        AssemblyBuilder ab = AppDomain.CurrentDomain.DefineDynamicAssembly(name, AssemblyBuilderAccess.Run);
        ModuleBuilder mb = ab.DefineDynamicModule(name.Name);
        TypeBuilder tb = mb.DefineType("DestructiveBombsOptions", TypeAttributes.Class, typeof(OptionalUI.OptionInterface));

        // Define the constructor to only call the base
        ConstructorBuilder cb = tb.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, new Type[] { typeof(Partiality.Modloader.PartialityMod) });
        ILGenerator ctorILG = cb.GetILGenerator();
        ctorILG.Emit(OpCodes.Ldarg_0);
        ctorILG.Emit(OpCodes.Ldarg_1);
        ctorILG.Emit(OpCodes.Call, typeof(OptionalUI.OptionInterface).GetConstructor(new Type[] { typeof(Partiality.Modloader.PartialityMod) }));
        ctorILG.Emit(OpCodes.Ret);

        // Define Initialize to call the base, then a proxy
        MethodBuilder initmb = tb.DefineMethod("Initialize", MethodAttributes.Virtual | MethodAttributes.Public | MethodAttributes.ReuseSlot | MethodAttributes.HideBySig);
        ILGenerator initmbILG = initmb.GetILGenerator();
        initmbILG.Emit(OpCodes.Ldarg_0);
        initmbILG.Emit(OpCodes.Call, typeof(OptionalUI.OptionInterface).GetMethod("Initialize", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));
        initmbILG.Emit(OpCodes.Ldarg_0);
        initmbILG.Emit(OpCodes.Call, typeof(DBOProxy).GetMethod("Initialize", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static));
        initmbILG.Emit(OpCodes.Ret);

        // Define ConfigOnChange to call the base, then a proxy
        MethodBuilder ccmb = tb.DefineMethod("ConfigOnChange", MethodAttributes.Virtual | MethodAttributes.Public | MethodAttributes.ReuseSlot | MethodAttributes.HideBySig);
        ILGenerator ccmbILG = ccmb.GetILGenerator();
        ccmbILG.Emit(OpCodes.Ldarg_0);
        ccmbILG.Emit(OpCodes.Call, typeof(OptionalUI.OptionInterface).GetMethod("ConfigOnChange", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));
        ccmbILG.Emit(OpCodes.Ldarg_0);
        ccmbILG.Emit(OpCodes.Call, typeof(DBOProxy).GetMethod("ConfigOnChange", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static));
        ccmbILG.Emit(OpCodes.Ret);

        oiType = tb.CreateType();
        Debug.Log("Loaded DestructiveBombsOptions");
    }
}

// This class normally would inherit from OptionsInterface
// If you try to load a class that inherits from ConfigMachine, though, the entire assembly will fail to load
public class DBOProxy
{
    // Add the mod UI
    public static void Initialize(OptionalUI.OptionInterface self)
    {
        self.Tabs = new OptionalUI.OpTab[1];
        self.Tabs[0] = new OptionalUI.OpTab("Config");

        OptionalUI.OpLabel cfgTitle = new OptionalUI.OpLabel(new Vector2(100, 550), new Vector2(400, 40), "CONFIG", FLabelAlignment.Center, true);
        self.Tabs[0].AddItem(cfgTitle);
        OptionalUI.OpLabel cfgRadiusMulLabel = new OptionalUI.OpLabel(new Vector2(200, 300), new Vector2(200, 40), "Radius Multiplier %", FLabelAlignment.Center);
        self.Tabs[0].AddItem(cfgRadiusMulLabel);
        OptionalUI.OpDragger cfgRadiusMulDragger = new OptionalUI.OpDragger(new Vector2(300 - 12, 260), "RadiusMultiplier", 100);
        cfgRadiusMulDragger.max = 500;
        cfgRadiusMulDragger.min = 0;
        self.Tabs[0].AddItem(cfgRadiusMulDragger);
    }

    // Apply changes to the mod
    public static void ConfigOnChange(OptionalUI.OptionInterface self)
    {
        DestructiveBombs.configRadiusMul = int.TryParse(OptionalUI.OptionInterface.config["RadiusMultiplier"], out int res) ? res / 100f : 1f;
    }
}