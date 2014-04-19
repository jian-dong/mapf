﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;

namespace CPF_experiment
{
    class CBS_old : ICbsSolver
    {
          // The key of the constraints list used for each CBS node
        public static string CONSTRAINTS = "constraints";
        // The key of the internal CAT for CBS
        public static string INTERNAL_CAT = "internalCAT";
        // The key of recent constraints
        public static string NEW_CONSTRAINTS = "newConstraints";
        // The key of recent internal CAT for CBS
        public static string NEW_INTERNAL_CAT = "newInternalCAT";

        protected ProblemInstance instance;
        public BinaryHeap openList;
        public Dictionary<CbsNode, CbsNode> closedList;
        public int highLevelExpanded;
        public int highLevelGenerated;
        public int totalCost;
        protected Run runner;
        protected CbsNode goalNode;
        protected Plan solution;
        protected int maxCost;
        protected int loweLevelExpanded;
        protected int loweLevelGenerated;
        protected ICbsSolver solver;
        protected ICbsSolver lowLevelSolver;
        protected int mergeThreshold;
        protected int minCost;
        protected int maxThreshold;
        protected int maxSizeGroup;
        protected HeuristicCalculator heuristic;

        public CBS_old() { }

        public CBS_old(ICbsSolver solver, int maxThreshold = -1, int currentThreshold = -1)
        {
            this.closedList = new Dictionary<CbsNode, CbsNode>();
            this.openList = new BinaryHeap();
            this.mergeThreshold = currentThreshold;
            this.solver = solver;
            this.lowLevelSolver = solver;
            this.maxThreshold = maxThreshold;
            if (currentThreshold < maxThreshold)
            {
                this.solver = new CBS_old(solver, maxThreshold, currentThreshold + 1);
            }
        }

        public virtual void Setup(ProblemInstance problemInstance, Run runner)
        {
            this.instance = problemInstance;
            this.runner = runner;
            CbsNode root = new CbsNode(instance.m_vAgents.Length);
            this.openList.Add(root);
            this.highLevelExpanded = 0;
            this.highLevelGenerated = 1;
            loweLevelExpanded = 0;
            loweLevelGenerated = 0;
            maxSizeGroup = 1;
            this.totalCost = 0;
            if (problemInstance.parameters.ContainsKey(Trevor.MAXIMUM_COST_KEY))
                this.maxCost = (int)(problemInstance.parameters[Trevor.MAXIMUM_COST_KEY]);
            else
                this.maxCost = int.MaxValue;
            if (problemInstance.parameters.ContainsKey(CBS_LocalConflicts.NEW_INTERNAL_CAT) == false)
            {
                problemInstance.parameters[CBS_LocalConflicts.INTERNAL_CAT] = new HashSet_U<TimedMove>();
                problemInstance.parameters[CBS_LocalConflicts.CONSTRAINTS] = new HashSet_U<CbsConstraint>();
                problemInstance.parameters[CBS_LocalConflicts.NEW_CONSTRAINTS] = new HashSet<CbsConstraint>();
                problemInstance.parameters[CBS_LocalConflicts.NEW_INTERNAL_CAT] = new HashSet<TimedMove>();
            }
            else
            {
                problemInstance.parameters[CBS_LocalConflicts.NEW_INTERNAL_CAT] = new HashSet<TimedMove>();
                problemInstance.parameters[CBS_LocalConflicts.NEW_CONSTRAINTS] = new HashSet<CbsConstraint>();
            }
            CbsNode.allConstraintsForNode = new HashSet<CbsConstraint>();
            minCost = 0;
        }

        public void Setup(ProblemInstance problemInstance, int minDepth, Run runner)
        {
            Setup(problemInstance, runner);
            this.minCost = minDepth;
        }

        public void SetHeuristic(HeuristicCalculator heuristic)
        {
            this.heuristic = heuristic;
            this.solver.SetHeuristic(heuristic);
        }

        public HeuristicCalculator GetHeuristic()
        {
            return this.heuristic;
        }

        public void Clear()
        {
            this.openList.Clear();
            this.closedList.Clear();
            this.instance = null;
            this.solver.Clear();
            CbsNode.allConstraintsForNode.Clear();
        }

        public virtual String GetName() 
        {
            if (mergeThreshold == -1)
                return "Basic CBS_old";
            return "CBS_old Local(" + mergeThreshold + ")(" + maxThreshold + ")+" + lowLevelSolver;
        }

        public int GetSolutionCost() { return this.totalCost; }

        public void OutputStatistics(TextWriter output)
        {
            output.Write(this.highLevelExpanded + Run.RESULTS_DELIMITER);
            output.Write(this.highLevelGenerated + Run.RESULTS_DELIMITER);
            output.Write("N/A" + Run.RESULTS_DELIMITER);
            output.Write("N/A" + Run.RESULTS_DELIMITER);
            output.Write("N/A" + Run.RESULTS_DELIMITER);
            output.Write(loweLevelExpanded + Run.RESULTS_DELIMITER);
            output.Write(Process.GetCurrentProcess().VirtualMemorySize64 + Run.RESULTS_DELIMITER + Run.RESULTS_DELIMITER);
        }

        public bool Solve()
        {
            //Debug.WriteLine("Solving Sub-problem On Level - " + mergeThreshold);
            //Console.ReadLine();

            CbsConflict conflict;
            CbsNode currentNode = (CbsNode)openList.Remove();
            highLevelExpanded++;
            if (currentNode.solve(instance,runner,minCost , lowLevelSolver ,ref highLevelExpanded,ref highLevelGenerated, ref loweLevelExpanded,ref loweLevelGenerated) == false)
            {
                return false;
            }
            if (currentNode.totalCost <= this.maxCost)
            {
                this.openList.Add(currentNode);
                this.closedList.Add(currentNode, currentNode);
                this.addToGlobalConflictCount(currentNode.getConflict());
            }
            while (openList.Count > 0 && runner.ElapsedMilliseconds() < Constants.MAX_TIME)
            {
                // Check if max time has been exceeded
                if (runner.ElapsedMilliseconds() > Constants.MAX_TIME)
                {
                    totalCost = Constants.TIMEOUT_COST;
                    Console.WriteLine("Out of time");
                    this.Clear();
                    return false;
                }
                currentNode = (CbsNode)openList.Remove();
                conflict = currentNode.getConflict();
                // Check if node is the goal
                if (conflict == null)
                {
                    this.totalCost = currentNode.totalCost;
                    this.goalNode = currentNode;
                    this.solution = currentNode.CalculateJointPlan();
                    this.Clear();
                    return true;
                }
                // Expand
                highLevelExpanded++;
                if (expand(currentNode, conflict))
                    if (currentNode.collapse != 0)
                        currentNode.clear();
            }
            totalCost = Constants.NO_SOLUTION_COST;
            this.Clear();
            return false;
        }

        protected virtual bool checkMerge(CbsNode node)
        {
            return node.checkMergeCondition(mergeThreshold);
        }

        public virtual bool expand(CbsNode node,CbsConflict conflict)
        {
            closedList.Remove(node);

            if (this.maxThreshold != -1 && checkMerge(node))
            {
                if (closedList.ContainsKey(node))
                    return true;
                if (node.rePlan(instance, runner, conflict.agentA, this.minCost, solver, lowLevelSolver, ref highLevelExpanded, ref highLevelGenerated, ref loweLevelExpanded, ref loweLevelGenerated) == false)
                {
                    if (node.replanSize > this.maxSizeGroup)
                        maxSizeGroup = node.replanSize;
                    return true;
                }
                if (node.replanSize > this.maxSizeGroup)
                    maxSizeGroup = node.replanSize;
                if (node.totalCost <= maxCost)
                    openList.Add(node);
                closedList.Add(node, node);
                this.addToGlobalConflictCount(node.getConflict());
                return false;
            }

            closedList.Add(node, node);

            CbsConstraint con;
            CbsNode toAdd;

            con = new CbsConstraint(conflict, instance, true);
            toAdd = new CbsNode(node, con, conflict.agentA, instance);

            if (closedList.ContainsKey(toAdd) == false)
            {

                if (toAdd.rePlan(instance, runner, conflict.agentA, Math.Max(minCost, conflict.timeStep), solver, lowLevelSolver, ref highLevelExpanded, ref highLevelGenerated, ref loweLevelExpanded, ref loweLevelGenerated))
                {
                    if (toAdd.totalCost <= this.maxCost)
                    {
                        openList.Add(toAdd);
                        closedList.Add(toAdd, toAdd);
                        this.highLevelGenerated++;
                        addToGlobalConflictCount(toAdd.getConflict());
                    }
                }
            }



            con = new CbsConstraint(conflict, instance, false);
            toAdd = new CbsNode(node, con, conflict.agentB, instance);

            if (closedList.ContainsKey(toAdd) == false)
            {
                if (toAdd.rePlan(instance, runner, conflict.agentB, Math.Max(minCost, conflict.timeStep), solver, lowLevelSolver, ref highLevelExpanded, ref highLevelGenerated, ref loweLevelExpanded, ref loweLevelGenerated))
                {
                    if (toAdd.totalCost <= this.maxCost)
                    {
                        openList.Add(toAdd);
                        closedList.Add(toAdd, toAdd);
                        this.highLevelGenerated++;
                        addToGlobalConflictCount(toAdd.getConflict());
                    }
                }

            }
            return true;
        }

        protected virtual void addToGlobalConflictCount(CbsConflict conflict) { }

        public virtual Plan GetPlan()
        {
            return this.solution;
        }

        private void PrintSolution(WorldState end)
        {
        }

        public int getSolutionDepth() { return -1; }
        public int getNodesPassedPruningCounter() { return loweLevelExpanded; }
        public int getNodesFailedOn2Counter() { return -1; }
        public int getNodesFailedOn3Counter() { return -1; }
        public int getNodesFailedOn4Counter() { return -1; }
        public long getMemoryUsed() { return Process.GetCurrentProcess().VirtualMemorySize64; }
        public WorldState GetGoal() { throw new NotSupportedException("CBS doesn't have a traditional goal state as it solves the problem independently for each agent"); }
        public SinglePlan[] getSinglePlans()
        {
            return goalNode.allSingleAgentPlans;
        }
        public int getHighLevelExpanded() { return highLevelExpanded; }
        public int getHighLevelGenerated() { return highLevelGenerated; }
        public int getLowLevelExpanded() { return loweLevelExpanded; }
        public int getLowLevelGenerated() { return loweLevelGenerated; }
        public int getMaxGroupSize()
        {
            return this.maxSizeGroup;
        }
    }
    class CBS_GlobalConflicts_OLD : CBS_old
    {
        int[][] globalConflictsCounter;

        public CBS_GlobalConflicts_OLD(ICbsSolver solver, int maxThreshold, int currentThreshold)
        {
            this.closedList = new Dictionary<CbsNode, CbsNode>();
            this.openList = new BinaryHeap();
            this.mergeThreshold = currentThreshold;
            this.solver = solver;
            this.lowLevelSolver = solver;
            this.maxThreshold = maxThreshold;
            if (currentThreshold < maxThreshold)
            {
                this.solver = new CBS_GlobalConflicts_OLD(solver, maxThreshold, currentThreshold + 1);
            }
        }

        public CBS_GlobalConflicts_OLD(ICbsSolver solver) : base(solver) { }

        public override void Setup(ProblemInstance problemInstance, Run runner)
        {
            globalConflictsCounter = new int[problemInstance.m_vAgents.Length][];
            for (int i = 0; i < globalConflictsCounter.Length; i++)
            {
                globalConflictsCounter[i] = new int[i];
                for (int j = 0; j < i; j++)
                {
                    globalConflictsCounter[i][j] = 0;
                }
            }
            base.Setup(problemInstance, runner);
        }

        protected override bool checkMerge(CbsNode node)
        {
            return node.checkMergeCondition(mergeThreshold, globalConflictsCounter);
        }

        protected override void addToGlobalConflictCount(CbsConflict conflict)
        {
            if (conflict != null)
                globalConflictsCounter[Math.Max(conflict.agentA, conflict.agentB)][Math.Min(conflict.agentA, conflict.agentB)]++;
        }

        public override string GetName()
        {
            if (mergeThreshold == -1)
                return "Basic CBS_OLD";
            return "CBS_OLD Global(" + mergeThreshold + ")(" + maxThreshold + ")+" + lowLevelSolver;
        }
    }
}
