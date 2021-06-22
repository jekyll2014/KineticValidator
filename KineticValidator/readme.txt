Please note this validator is not and AI an is not intended to replace the developer. It's just making notes on suspicious code parts to look at.

Validations implemented:
	1) File list validation - validation is always done on project files search and load. Will be excluded from report if not checked.
	2) Serialization validation - validation is always done on files deserialization. Will be excluded from report if not checked.
	3) JSON parser validation - validation is always done on JSON tree to flat list conversion. Will be excluded from report if not checked.
	4) Schema validation - as soon as schemes are not completely correct I'd recommend to enable "Skip annoying schema errors" to skip some errors which are false usually (not always). Also it's possible to bypass HTTPS authentication error if schema server will ever have incorrect certificate again.
	5) Redundant files - .jsonc files not imported in any of the project files.
	6) National characters - non-ASCII chars in the JSON properties names/values (not touching comments). it's a possible problem for Russian programmers as they have some national characters very similar to English ones so it's possible to have incorrect words absolutely indistinguishable on sight. Also there are some spaces replaced with "&nbsp" char.
	7) Duplicate JSON property names - JSON properties with similar names within one JSON object. Could not be detected by normal parser AFAIK.
	8) Empty patch names - not sure this can happen but it's just an idea
	9) Redundant patches - %patch% not used anywhere in the code
	10) Non existing patches - a call to undefined %patch%
	11) Overriding patches - ignores shared patches override
	12) Possible patches - property values similar to any of the defined patches. Can be an option to use %patch% instead of hard-coded value.
	13) Hard-coded message strings - could be a good idea to move them to resource file strings.jsonc
	14) Possible strings - property values similar to any of the defined string resources. Can be an option to use "{{strings.*}}" instead of hard-coded value.
	15) Apply patches - replace all %patch% usages with values to improve further validations. Turning this off results to a worse error detection in general case.
	16) Empty string names - not sure this can happen but it's just an idea
	17) Empty string values - not sure this can happen but it's just an idea
	18) Redundant strings - string resources not used anywhere in the project
	19) Non existing strings - a call to string resource not defined in strings.jsonc
	20) Overriding strings - why would one do it?
	21) Empty event names - not sure this can happen but it's just an idea
	22) Empty events - events having no any "type" action inside.
	23) Overriding events - events overriding each other inside the project files (copy-paste?) or events overriding imported events in case imported one is not empty (have any "type" action inside).
	24) Redundant events - events not used anywhere in the project
	25) Non existing events - a call to not defined event
	26) Empty dataView names - not sure this can happen but it's just an idea
	27) Redundant dataViews - dataViews not used anywhere in the project
	28) Non existing dataViews - a call to string resource not defined in dataviews.jsonc
	29) Overriding dataViews - there are only a few reasons to do it. For example - attach a menu tools.
	30) Non existing dataTables - a call to dataTables not defined in strings.jsonc
	31) Empty rule names - not sure this can happen but it's just an idea
	32) Overriding rules - why would one do it?
	33) Empty tool names - not sure this can happen but it's just an idea
	34) Overriding tools - why would one do it?
	35) Missing forms - call to forms not having certain folder + events.jsonc file (not implemented in Kinetic). This could be a WinForm name so not an issue.
	36) Missing searches - call to search forms not having certain \Shared\search\like\searchForm\search.jsonc file (not implemented in Kinetic).
	37) JavaScript code - any property value containing "#_" pattern
	38) JS #_trans.dataView('DataView').count_# - old-style version of a newer "%DataView.count%" macro
	39) Incorrect dataview-condition - "dataview-condition" method must have different values for "dataview" and "result" properties otherwise dataview data could be corrupted.
	40) Incorrect REST calls - to detect inconsistent REST calls. Available services/methods/parameters are taken from Server\Assemblies folder *.Contract*.dll files. Need to have some dependent DLL's in program or Assemblies folder to get parameters list for the methods (service and methods works well without). See 'Tech notes'.
	41) Incorrect field names - checking every {dataview.field} pattern for defined dataView and existing field as per server dataset information. Field check is only performed for server-related dataTables/views.
	42) Missing layout id's - any layouts not having the "model.id" or "model.id" different from control "id"
	43) Incorrect event expressions - <condition> event expression validation (C# style)
	44) Incorrect rule conditions - <dataview-condition> event expression and rule condition validation (SQL style)
	45) !!!Experimental feature!!! Incorrect layout id's - any layouts having "model.id" different from control "id"
	46) !!!Experimental feature!!! Incorrect tab links - to detect inexistent tab id's (broken by AppStudio). Not very reliable - false alarms on slide-outs.
	47) !!!Experimental feature!!! Duplicate GUID's - to find GUID duplicates within project scope.

	It is possible to select root projects folder to run mass-validation. Note that it tries to find a project in only one sub-folder level.
	Program also tries to load report from \program_folder\project_folder\ on project folder selection. This is a previously generated folder so the report can be outdated.

Option flags:
	"Skip annoying schema errors" - to skip some errors which are false usually (not always) due to our schema files are not consistent/updated.
	"Ignore HTTPS error" - to bypass HTTPS authentication error if schema server will have incorrect certificate again
	"Always on top" - to make program and editor(s) window on top of others.
	"Reformat JSON" - to beautify JSON text in the editor window(s). Note that it takes "file -> JSON object -> formatted text" conversion so some JSON format issues will not be visible in the editor window even if any detected (JSON parser normalizes the content).
	"Show preview" - to reload file in editor window(s) on report line selected.
	"Save report" - save report file in \program_folder\project_folder\.
	"Save data files" - keep temporary files in \program_folder\project_folder\.

Program settings:
	\KineticValidator.exe.config
		SystemMacros - system macro list to ignore by patch validators (like "%value%")
		SystemDataViews - system dataViews to ignore by undefined dataViews validator (like "sysTools").
		InitialProjectFiles - list of files to try loading as a root files.
		IgnoreSchemaErrors - list of error types to ignore by schema validator. See full list in NJsonSchema.Validation.ValidationErrorKind enum.
	\program_folder\project_folder\ignore.json
		Ignore list used to filter specified errors for certain project only. Empty field means any value.
	\program_folder\ignore.json
		Global ignore list used to filter specified errors in every project. Empty field means any value.

Tech. notes:
	- Program window and editors window dimensions are saved in .config on program close so user can arrange comfortable UI layout once. The same applies to the "Message" column width of the report grid, last project folder, option flags and list of selected validators.
	- Validation report is saved into \program_folder\project_folder\report.json.
	- Schema files are saved into \schemas\ folder and can be used to work off-line (remove ".original" suffix). It's possible to edit schema if one have any idea to improve.
	- Double-click/select any column except "LineId" to see the original file. Error-related entries will be highlighted.
	- Double-click/select "LineId" column to see compiled file with "LineId" highlighted (helps in case there is no highlight for original files ore one need to see all entities of the certain type (events/rules/...) collected in one file ).
	- Right click brings up menu to delete unwanted row from report table or add it to project ignore list. Edit ignore list manually to allow some fields to be "any"
	- Entity collections saved into \program_folder\project_folder\*.txt and *.json files (basically the same we get in \Cache). One can check what program has collected to analyze. *.txt also used to show error in the text editor by "LineId".
	- Program can be run from command line (see /h for help). Settings by default are taken from .config, so it's possible to run GUI to setup it once.
	- REST call validation may need the following DLL's to have in the program folder:
		Epicor.ServiceModel.dll
		Erp.Common.ContractInterfaces.dll
		Erp.Contracts.BO.JobEntry.dll
		Ice.Contracts.Lib.ClassAttribute.dll
		...and the above list can be changed due to a dependency changes. Please try update_dlls.bat to collect DLL's needed. Note the list of DLL's could be changed any time.
	- Assembly load exceptions will be saved to DeveloperLog.txt file so you can check it to see which assemblies are missing.
	- Expression/condition validation is not intended to validate complex expressions with JS code inside. Please use "#_ _#" bracketing to mark JS expressions.

P.S. Program is not displaying any text on the screen now in command line mode as I don't know how to manage both cmd.line and GUI mode in one executable - it has nagging black window if I set cmdline or no console output in GUI mode. Console output not displayed but still available to redirect into the file (like "KineticValidator.exe /? > help.txt").
