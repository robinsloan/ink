﻿using System;
using System.Collections.Generic;
using System.Text;

namespace Inklewriter.Parsed
{
	public class Story : FlowBase
    {
        public override FlowLevel flowLevel { get { return FlowLevel.Story; } }
        public bool hadError { get; protected set; }

        public Story (List<Parsed.Object> toplevelObjects) : base(null, toplevelObjects)
		{
            // Don't do anything on construction, leave it lightweight until
            // the ExportRuntime method is called.
		}

        // Before this function is called, we have IncludedFile objects interspersed
        // in our content wherever an include statement was.
        // So that the include statement can be added in a sensible place (e.g. the
        // top of the file) without side-effects of jumping into a knot that was
        // defined in that include, we separate knots and stitches from anything
        // else defined at the top scope of the included file.
        // 
        // Algorithm: For each IncludedFile we find, split its contents into
        // knots/stiches and any other content. Insert the normal content wherever
        // the include statement was, and append the knots/stitches to the very
        // end of the main story.
        protected override void PreProcessTopLevelObjects(List<Parsed.Object> topLevelContent)
        {
            var flowsFromOtherFiles = new List<FlowBase> ();

            // Inject included files
            int i = 0;
            while (i < topLevelContent.Count) {
                var obj = topLevelContent [i];
                if (obj is IncludedFile) {

                    var file = (IncludedFile)obj;

                    // Remove the IncludedFile itself
                    topLevelContent.RemoveAt (i);

                    // When an included story fails to load, the include
                    // line itself is still valid, so we have to handle it here
                    if (file.includedStory != null) {
                        
                        var nonFlowContent = new List<Parsed.Object> ();

                        var subStory = file.includedStory;
                        foreach (var subStoryObj in subStory.content) {
                            if (subStoryObj is FlowBase) {
                                flowsFromOtherFiles.Add ((FlowBase)subStoryObj);
                            } else {
                                nonFlowContent.Add (subStoryObj);
                            }
                        }

                        // Add contents of the file in its place
                        topLevelContent.InsertRange (i, nonFlowContent);

                        // Skip past the lines of this sub story
                        // (since it will already have recursively included
                        //  any lines from other files)
                        i += nonFlowContent.Count;
                    }
                    
                }
                i++;
            }

            // Add the flows we collected from the included files to the
            // end of our list of our content
            topLevelContent.AddRange (flowsFromOtherFiles);

        }

        void GatherAllKnotAndStitchNames(List<Parsed.Object> fromContent)
        {
            foreach (var obj in fromContent) {
                var subFlow = obj as FlowBase;
                if (subFlow != null) {
                    _allKnotAndStitchNames.Add (subFlow.dotSeparatedFullName);
                    GatherAllKnotAndStitchNames (subFlow.content);
                }
            }
        }

        public override bool HasOwnVariableWithName(string varName, bool allowReadCounts = true)
        {
            if (allowReadCounts) {
                if (_allKnotAndStitchNames.Contains (varName)) {
                    return true;
                }
            }

            return base.HasOwnVariableWithName (varName, allowReadCounts);
        }

		public Runtime.Story ExportRuntime()
		{
            // Gather all FlowBase definitions as variable names
            _allKnotAndStitchNames = new HashSet<string>();
            GatherAllKnotAndStitchNames (content);

			// Get default implementation of runtimeObject, which calls ContainerBase's generation method
            var rootContainer = runtimeObject as Runtime.Container;

			// Replace runtimeObject with Story object instead of the Runtime.Container generated by Parsed.ContainerBase
			var runtimeStory = new Runtime.Story (rootContainer);
			runtimeObject = runtimeStory;

			// Now that the story has been fulled parsed into a hierarchy,
			// and the derived runtime hierarchy has been built, we can
			// resolve referenced symbols such as variables and paths.
			// e.g. for paths " -> knotName --> stitchName" into an INKPath (knotName.stitchName)
			// We don't make any assumptions that the INKPath follows the same
			// conventions as the script format, so we resolve to actual objects before
			// translating into an INKPath. (This also allows us to choose whether
			// we want the paths to be absolute)
			ResolveReferences (this);

			// Don't successfully return the object if there was an error
            if (hadError) {
				return null;
			}

			return runtimeStory;
		}

        // Initialise all read count variables for every knot and stitch name
        // TODO: This seems a bit overkill, to mass-generate a load of variable assignment
        // statements. Could probably just include a bespoke "initial variable state" in
        // the story, or even just a list of knots/stitches that the story automatically
        // initialises.
        protected override void OnRuntimeGenerationDidStart(Runtime.Container container)
        {
            container.AddContent (Runtime.ControlCommand.EvalStart());

            foreach (string flowName in _allKnotAndStitchNames) {
                container.AddContent (new Runtime.LiteralInt(0));
                container.AddContent (new Runtime.VariableAssignment (flowName, true));
            }

            container.AddContent (Runtime.ControlCommand.EvalEnd());

            // FlowBase handles argument variable assignment and read count updates
            base.OnRuntimeGenerationDidStart(container);
        }


		public override void Error(string message, Parsed.Object source)
		{
            var sb = new StringBuilder ();
            sb.Append ("ERROR: ");
            sb.Append (message);
            if (source != null && source.debugMetadata != null && source.debugMetadata.startLineNumber >= 1 ) {
                sb.Append (" on "+source.debugMetadata.ToString());
            }
            Console.WriteLine (sb.ToString());
            hadError = true;
		}

        public void ResetError()
        {
            hadError = false;
        }

        HashSet<string> _allKnotAndStitchNames;
	}
}

