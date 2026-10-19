using System;
using System.Collections.Generic;

// 修改复用自CSduckie/2DArpg-Portfolio：保留泛型切换和每个角色自己的状态缓存。
public class StateMachine
{
    private IStateMachineOwner owner;
    private readonly Dictionary<Type, StateBase> stateDic = new Dictionary<Type, StateBase>();
    private StateBase currentState;

    public Type CurrentStateType { get { return currentState != null ? currentState.GetType() : null; } }
    public bool hasState { get { return currentState != null; } }
    public StateBase CurrentState { get { return currentState; } }

    public void Init(IStateMachineOwner owner)
    {
        if (owner == null)
        {
            throw new ArgumentNullException(nameof(owner));
        }
        if (ReferenceEquals(this.owner, owner))
        {
            return;
        }
        Stop();
        this.owner = owner;
    }

    public bool ChangeState<T>(bool reCurrstate = false) where T : StateBase, new()
    {
        if (owner == null || (hasState && CurrentStateType == typeof(T) && !reCurrstate))
        {
            return false;
        }

        // 保留原顺序：旧状态退出后才创建目标，最后进入；Enter/Exit不递归切换。
        currentState?.Exit();
        currentState = GetState<T>();
        currentState.Enter();
        return true;
    }

    private StateBase GetState<T>() where T : StateBase, new()
    {
        Type type = typeof(T);
        if (!stateDic.TryGetValue(type, out StateBase state))
        {
            state = new T();
            state.Init(owner);
            stateDic.Add(type, state);
        }
        return state;
    }

    public void Stop()
    {
        // 先清运行引用，使未启动、重复停用和停用后销毁都不会重复Exit/UnInit。
        StateBase previousState = currentState;
        currentState = null;
        owner = null;
        List<StateBase> createdStates = new List<StateBase>(stateDic.Values);
        stateDic.Clear();

        try
        {
            previousState?.Exit();
        }
        finally
        {
            foreach (StateBase state in createdStates)
            {
                state.UnInit();
            }
        }
    }
}
