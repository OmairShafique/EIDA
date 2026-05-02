function meta_regression_generalized(filename, predictorVars, damageStates, groupingVar, responseVarPrefix,saveName)

%% Initializing

clc

diary('meta_regression.text'); % saving all proceeds to text file

figsBefore = findall(0, 'Type', 'figure');

%% Step 1: Load and Prepare Data
% ---------------------------------------------------------------
% This step reads the fragility table from the input file.
% The saving_data() function is expected to return:
%   fragilityTable : table containing predictor and response variables
%   dataTable      : (optional) raw or intermediate table
%
% REQUIREMENTS:
%   - `filename` must be defined in the workspace.
%   - `predictorVars` is a cell array of predictor variable names (strings).
%   - `groupingVar` is the name of the grouping variable for random effects.
%   - `damageStates` is a cell array of m damage state response variable names.
% --


try
    % [~,data] = saving_data(filename);

    opts = detectImportOptions(filename);
    data = readtable(filename, opts);

    %% Step 2: Preprocess Predictor Variables
    % ---------------------------------------------------------------
    % This step ensures that all predictors used in the GLME models
    % are in categorical format (if applicable), which is important
    % for interpreting levels and applying consistent coefficients.
    %
    % - For each predictor in `predictorVars`, we:
    %       1. Create a categorical version (appending "_cat" to the name).
    %       2. Remove empty categories (removecats).
    %       3. Replace undefined values with <missing>.
    %
    % - For the grouping variable, we ensure it is categorical
    %   but DO NOT remove categories so all levels are retained
    %   for consistent predictions later.
    % ---------------------------------------------------------------

    % Store category labels for each predictor (for later coefficient mapping)

    for pv = predictorVars
        varName = pv{1};
        data.([varName '_cat']) = categorical(data.(varName));
        % Optional: Check for empty categories which can cause issues
        data.([varName '_cat']) = removecats(data.([varName '_cat']));
        if any(isundefined(data.([varName '_cat'])))
            fprintf('Warning: Found undefined entries in predictor "%s". Replacing with <missing>.\n', varName);
            data.([varName '_cat'])(isundefined(data.([varName '_cat']))) = missing;
        end

    end

    % Convert SNo to categorical for grouping in fitglme if it's not already
    if ~iscategorical(data.(groupingVar))
        data.(groupingVar) = categorical(data.(groupingVar));
        % Keep all study levels from original dataTable for consistent prediction later
        % dataTable.(groupingVar) = removecats( dataTable.(groupingVar)); % Don't remove here
    end


    % Step 2.5: Preallocate Storage for Coefficients and Models
    % ---------------------------------------------------------------
    % For generalization:
    %   - Rows = levels of each predictor
    %   - Columns = number of damage states
    %
    % We store:
    %   - interceptCoefs (1 x m)
    %   - coefMatrices for each predictor (stored in a struct)
    %   - GLME models (1 x m cell array)
    % ---------------------------------------------------------------

    interceptCoefs = nan(1, length(damageStates));

    % Struct to hold coefficient matrices for each predictor
    coefMatrices = struct();
    predictorCategoryLabels = struct(); % Store category labels for each predictor

    for i = 1:length(predictorVars)
        % Extract predictor variable name from cell array
        varName = predictorVars{i};

        % Create category labels dynamically based on the _cat variable
        catVarName = [varName '_cat'];  % e.g., "Story_cat"

        % Ensure the categorical variable exists
        if ~iscategorical(data.(catVarName))
            error('Expected %s to be categorical, but it is not.', catVarName);
        end

        % Store the category labels for this predictor
        predictorCategoryLabels.(varName) = categories(data.(catVarName));

        % Get number of levels for this predictor
        numLevels = length(predictorCategoryLabels.(varName));

        % Preallocate coefficient matrix for this predictor
        % Rows = predictor levels, Columns = damage states
        coefMatrices.(varName) = nan(numLevels, length(damageStates));
    end

    % Storage for fitted GLME models
    glmeModels = cell(1, length(damageStates));


    % ================================
    %%  STEP 3: Generalized GLME Fitting
    %  Handles ANY number of predictors (n)
    %  and ANY number of damage states (m)
    %  ------------------------------------
    %  Requires:
    %   - damageStates: cell array of response var names
    %   - predictorVars: cell array of predictor base names
    %   - predictorCategoryLabels: struct with category labels for each predictor
    %   - groupingVar: string, name of grouping variable
    %   - fragilityTable: table with predictors, responses, and grouping var
    %  ------------------------------------
    %  Output:
    %   - glmeModels: cell array of fitted models (per damage state)
    %   - interceptCoefs: vector of intercepts (per damage state)
    %   - coefMatrices: struct of predictor coefficients
    % ================================

    for i = 1:length(damageStates)

        %%% ------------------------------------------------
        %%% 1) Extract the current response variable name
        %%% ------------------------------------------------
        current_context = damageStates{i};          % e.g., 'DS1'
        currentResponseVarName = [current_context,'_', responseVarPrefix];   % Keep naming consistent

        fprintf('\n--------------\n');
        fprintf('Processing Response: %s\n', currentResponseVarName);
        fprintf('--------------\n');

        %%% ------------------------------------------------
        %%% 2) Check that the response exists in the table
        %%% ------------------------------------------------
        if ~ismember(currentResponseVarName, data.Properties.VariableNames)
            fprintf('Skipping %s: Response variable not found.\n', currentResponseVarName);
            continue; % Go to next damage state
        end

        %%% ------------------------------------------------
        %%% 3) Remove rows where the response is NaN
        %%% ------------------------------------------------
        tempData = data(~isnan(data.(currentResponseVarName)), :);
        fprintf('%d rows remain after removing NaNs in "%s".\n', height(tempData), currentResponseVarName);

        if height(tempData) < 2
            fprintf('Skipping %s: Not enough data.\n', currentResponseVarName);
            continue; % Go to next damage state
        end

        %%% ------------------------------------------------
        %%% 4) Build modeling table dynamically
        %%% ------------------------------------------------
        % Append "_cat" to predictor base names to get categorical variable names
        predictorCatVars = strcat(predictorVars, '_cat');

        % Create new table with:
        %   - Response variable (renamed as "CurrentResponse" for formula consistency)
        %   - All predictors (in their categorical form)
        %   - Grouping variable
        tbl = table(tempData.(currentResponseVarName), 'VariableNames', {'CurrentResponse'});
        for p = 1:length(predictorCatVars)
            tbl.(predictorVars{p}) = tempData.(predictorCatVars{p});
        end
        tbl.(groupingVar) = tempData.(groupingVar);

        %%% ------------------------------------------------
        %%% 5) Remove rows with missing predictor/grouping data
        %%% ------------------------------------------------
        initialCount = height(tbl);
        tbl = rmmissing(tbl); % Removes any rows with NaNs/missing values
        if height(tbl) < 2
            fprintf('Skipping %s: Not enough rows after removing missing values.\n', currentResponseVarName);
            continue;
        end
        if initialCount - height(tbl) > 0
            fprintf('Removed %d rows with missing values.\n', initialCount - height(tbl));
        end

        %%% ------------------------------------------------
        %%% 6) Build GLME formula dynamically
        %%% ------------------------------------------------
        % Example: "CurrentResponse ~ 1 + Story + BayX + BayY + (1|SNo)"
        formulaParts = ['1', predictorVars]; % Always include intercept (1)
        fixedEffects = strjoin(formulaParts, ' + ');
        modelFormula = sprintf('CurrentResponse ~ %s + (1|%s)', fixedEffects, groupingVar);

        %%% ------------------------------------------------
        %%% 7) Fit the GLME model
        %%% ------------------------------------------------
        fprintf('Fitting model: %s\n', modelFormula);
        glme = fitglme(tbl, modelFormula, 'FitMethod', 'REMPL'); % 'REMPL' is faster for large data
        glmeModels{i} = glme; % Store the fitted model

        % Extract coefficient table from model
        coefTable = glme.Coefficients;

        %%% ------------------------------------------------
        %%% 8) Store intercept coefficient
        %%% ------------------------------------------------
        interceptRow = strcmp(coefTable.Name, '(Intercept)');
        if any(interceptRow)
            interceptCoefs(1, i) = coefTable.Estimate(interceptRow);
        end

        %%% ------------------------------------------------
        %%% 9) Extract coefficients for ALL predictors
        %%% ------------------------------------------------
        % Loop over predictors so the code works for any "n"
        for p = 1:length(predictorVars)
            varName = predictorVars{p};                  % e.g., 'Story'
            labels = predictorCategoryLabels.(varName);  % Get category labels
            tempCoefs = nan(length(labels), 1);          % Preallocate
            tempCoefs(1) = 0;                            % Reference level always 0

            % For each level (except reference), find coefficient in table
            for k = 2:length(labels)
                coefName = sprintf('%s_%s', varName, char(labels{k}));
                matchRow = strcmp(coefTable.Name, coefName);
                if any(matchRow)
                    tempCoefs(k) = coefTable.Estimate(matchRow);
                end
            end

            % Store in coefMatrices struct
            coefMatrices.(varName)(:, i) = tempCoefs;
        end
    end


    %% Step 4: Results Tables (Generalized for n predictors and m damage states)
    fprintf('\n=============================================================\n');
    fprintf('           META-REGRESSION COEFFICIENT SUMMARY\n');
    fprintf('=============================================================\n');
    fprintf(['Coefficients represent the estimated change in the specific response ' ...
        'variable for each column (e.g., DS1) relative to the reference level for each factor.\n']);
    fprintf('Reference levels have a coefficient of 0 by definition.\n\n');

    % -----------------------
    % Create variable names for results tables based on damageStates
    % (e.g., DS1_IM_Coef, DS2_IM_Coef, etc.)
    % -----------------------
    dsVarNames = cellfun(@(ds) sprintf('%s_%s_Coef', ds, responseVarPrefix), ...
        damageStates, 'UniformOutput', false);

    % -----------------------
    % Identify valid damage state columns (i.e., models successfully fitted)
    % A valid column has at least one non-NaN coefficient in intercept
    % -----------------------
    validContextCols = find(~all(isnan(interceptCoefs), 1));

    if isempty(validContextCols)
        % If no valid models exist for any DS, print a message and exit this section
        fprintf(['No models were successfully fitted for any damage state ' ...
            'context with the specified response variable pattern.\n']);
    else
        % ============================================================
        % 1) Display Intercept Coefficients
        % ============================================================
        fprintf('--- Intercept Coefficients ---\n');
        interceptTable = array2table(interceptCoefs(:, validContextCols), ...
            'VariableNames', dsVarNames(validContextCols));
        disp(interceptTable);
        fprintf('\n');

        % ============================================================
        % 2) Loop through all predictors dynamically
        % Each predictor’s coefficients are stored in coefMatrices.(predictorName)
        % and their category labels are in predictorCategoryLabels.(predictorName)
        % ============================================================
        predictorNames = fieldnames(coefMatrices); % All predictor variable names

        for p = 1:length(predictorNames)
            predictor = predictorNames{p}; % e.g., 'Story_cat', 'BayX_cat', etc.

            fprintf('--- %s Coefficients ---\n', predictor);

            % Get category labels for this predictor
            factorLabels = predictorCategoryLabels.(predictor);

            % Extract the coefficient matrix for this predictor
            % Keep only the valid DS columns
            coefMatrix = coefMatrices.(predictor)(:, validContextCols);

            % Convert numeric coefficients to cell format for table creation
            coefCell = num2cell(coefMatrix, 1);

            % Build results table: First column = factor labels, then DS coefficients
            resultsTable = table(factorLabels(:), coefCell{:}, ...
                'VariableNames', [{predictor} dsVarNames(validContextCols)]);

            % Display the results table for this predictor
            disp(resultsTable);

            % Display the reference level (always first category in factorLabels)
            fprintf('Reference %s Level (Coefficient = 0): %s\n\n', ...
                predictor, char(factorLabels{1}));
        end
    end


    %% Step 5: Visualizations (Coefficient Plots)
    fprintf('=============================================================\n');
    fprintf('                 VISUALIZATIONS (COEFFICIENTS)\n');
    fprintf('=============================================================\n');

    % If no valid model fits exist, skip plotting
    if isempty(validContextCols)
        fprintf('No models were successfully fitted. No coefficient plots will be generated.\n');
    else
        % ============================================================
        % Loop through all predictors dynamically
        % Each predictor’s coefficient matrix is in coefMatrices.(predictorName)
        % Each predictor’s factor labels are in predictorCategoryLabels.(predictorName)
        % ============================================================
        predictorNames = fieldnames(coefMatrices); % e.g., 'Story_cat', 'BayX_cat', etc.

        for p = 1:length(predictorNames)
            predictor = predictorNames{p}; % Current predictor name

            % Get factor labels for this predictor
            factorLabels = predictorCategoryLabels.(predictor);

            % Get coefficient matrix for this predictor (keep only valid DS cols)
            coefMatrix = coefMatrices.(predictor)(:, validContextCols);

            % Extract corresponding damage state labels for valid columns
            validDSLabels = damageStates(validContextCols);

            % --------------------------------------------------------
            % Call user-defined plotCoefficients function
            % Arguments:
            %   coefMatrix     - Coefficients for each factor level x damage state
            %   factorLabels   - Category labels (y-axis labels)
            %   validDSLabels  - Damage state names for legend
            %   predictor      - Predictor name for plot title
            %   responseVarPrefix - Variable name for labeling axes
            % --------------------------------------------------------
            plotCoefficients(coefMatrix, factorLabels, validDSLabels, predictor, responseVarPrefix);
        end
    end


    %% Step 6: Display the Fixed-Effects Model Equations Dynamically
    fprintf('\n=============================================================\n');
    fprintf('                  MODEL EQUATION (Fixed Effects)\n');
    fprintf('=============================================================\n');

    % Find indices of successfully fitted models
    successfullyFittedIndices = find(~cellfun(@isempty, glmeModels));

    if isempty(successfullyFittedIndices)
        fprintf('No models were successfully fitted, so no equation can be displayed.\n');
    else
        fprintf('Successfully fitted models for contexts: ');
        fprintf('%s ', damageStates{successfullyFittedIndices});
        fprintf('\n');

        % Get list of predictors dynamically from coefMatrices
        predictorNames = fieldnames(coefMatrices);

        % Loop through each successfully fitted model
        for idx = successfullyFittedIndices
            currentModel = glmeModels{idx};
            currentContext = damageStates{idx};
            currentResponse = sprintf('%s_%s', currentContext, responseVarPrefix);

            coefTable = currentModel.Coefficients;

            fprintf('Equation for response variable: %s (Context: %s)\n', ...
                currentResponse, currentContext);
            fprintf('Predicted %s = ', currentResponse);

            % ---------------------------
            % Get Intercept
            % ---------------------------
            interceptRow = strcmp(coefTable.Name, '(Intercept)');
            if any(interceptRow)
                interceptVal = coefTable.Estimate(interceptRow);
                fprintf('%.4f', interceptVal);
            else
                interceptVal = NaN;
                fprintf('Intercept_Not_Found');
            end

            % ---------------------------
            % Add terms for each predictor
            % ---------------------------
            for p = 1:length(predictorNames)
                predictor = predictorNames{p};
                labels = predictorCategoryLabels.(predictor);
                refLevel = labels{1}; % First category = reference level

                % Loop through non-reference levels
                for lvl = 2:length(labels)
                    currentLevel = labels{lvl};
                    expectedCoefName = sprintf('%s_%s', predictor, currentLevel);

                    rowMatch = strcmp(coefTable.Name, expectedCoefName);
                    if any(rowMatch)
                        coefVal = coefTable.Estimate(rowMatch);
                        if ~isnan(coefVal)
                            if coefVal >= 0
                                fprintf(' + %.4f', coefVal);
                            else
                                fprintf(' - %.4f', abs(coefVal));
                            end
                            fprintf(' * I(%s == ''%s'')', predictor, currentLevel);
                        end
                    end
                end

                % Break line between predictors for readability
                fprintf('\n    ');
            end

            % ---------------------------
            % Add Random Effect description
            % ---------------------------
            fprintf('+ Random Effect(StudyID)\n');
            fprintf('-------------------------------------------------------------\n');
            fprintf('Where I(Condition) is an indicator function (1 if true, 0 otherwise).\n');
            fprintf('Reference Levels (Coefficient = 0):\n');
            for p = 1:length(predictorNames)
                labels = predictorCategoryLabels.(predictorNames{p});
                fprintf('  %s: %s\n', predictorNames{p}, labels{1});
            end
            fprintf('The Random Effect(StudyID) accounts for between-study variation.\n');
            fprintf('For a new study not in the data, set Random Effect(StudyID) to 0.\n');
            fprintf('=============================================================\n');
        end
    end

    
    

    %% Step 7: Plot Predicted Response vs EACH Categorical Predictor
    %  Goal: replicate "DSx vs Story count" but generalized to ALL predictors
    %  ----------------------------------------------------------------
    fprintf('\n=============================================================\n');
    fprintf('   PLOTTING PREDICTED RESPONSE vs EACH PREDICTOR (GENERALIZED)\n');
    fprintf('=============================================================\n');

    if isempty(successfullyFittedIndices)
        fprintf('No models were successfully fitted; skipping prediction plots.\n');
    else
        % Get all predictor names from the struct containing their category labels
        allPredictors = fieldnames(predictorCategoryLabels);

        % Loop through each predictor and make a separate plot
        for predIdx = 1:numel(allPredictors)

            predictorToSweep = allPredictors{predIdx}; % current predictor name
            sweepLevels      = predictorCategoryLabels.(predictorToSweep);
            numSweepLevels   = numel(sweepLevels);

            % ------------------------------------------------------------
            % Hold all OTHER predictors fixed at their reference levels
            % (reference level = first category in their label list)
            % ------------------------------------------------------------
            fixedLevels = struct();
            for p = 1:numel(allPredictors)
                pr = allPredictors{p};
                fixedLevels.(pr) = predictorCategoryLabels.(pr){1}; % default = reference
            end

            % (Optional) You can add overrides for specific predictors here:
            % if strcmp(predictorToSweep,'BayX') && any(strcmp(predictorCategoryLabels.BayX,'BayX4'))
            %     fixedLevels.BayX = 'BayX4';
            % end

            % ------------------------------------------------------------
            % Allocate matrix for predictions:
            % rows = swept predictor levels
            % cols = successfully fitted damage states
            % ------------------------------------------------------------
            numGoodDS = numel(successfullyFittedIndices);
            predictedValues = nan(numSweepLevels, numGoodDS);

            % ------------------------------------------------------------
            % Compute fixed-effects predictions for each damage state model
            % ------------------------------------------------------------
            for c = 1:numGoodDS
                dsIdx    = successfullyFittedIndices(c);
                model    = glmeModels{dsIdx};
                coefTab  = model.Coefficients;

                % Intercept term
                interceptRow = strcmp(coefTab.Name, '(Intercept)');
                intercept    = 0;
                if any(interceptRow)
                    intercept = coefTab.Estimate(interceptRow);
                else
                    fprintf('Warning: Intercept not found for %s — using 0.\n', damageStates{dsIdx});
                end

                % Sum offsets for non-swept predictors (fixed)
                fixedOffsetSum = 0;
                for p = 1:numel(allPredictors)
                    pr = allPredictors{p};
                    if strcmp(pr, predictorToSweep), continue; end
                    refLevel = predictorCategoryLabels.(pr){1};
                    chosen   = fixedLevels.(pr);
                    if ~strcmp(chosen, refLevel)
                        coefName = sprintf('%s_%s', pr, chosen);
                        row = strcmp(coefTab.Name, coefName);
                        if any(row)
                            fixedOffsetSum = fixedOffsetSum + coefTab.Estimate(row);
                        end
                    end
                end

                % Sweep the current predictor across all its levels
                refSweepLevel = sweepLevels{1};
                for s = 1:numSweepLevels
                    levelName   = sweepLevels{s};
                    levelOffset = 0;
                    if ~strcmp(levelName, refSweepLevel)
                        coefName = sprintf('%s_%s', predictorToSweep, levelName);
                        row = strcmp(coefTab.Name, coefName);
                        if any(row)
                            levelOffset = coefTab.Estimate(row);
                        end
                    end
                    predictedValues(s, c) = intercept + fixedOffsetSum + levelOffset;
                end
            end

            % ------------------------------------------------------------
            % Plot: one curve per successfully fitted damage state
            % ------------------------------------------------------------
            figure; hold on;
            colors = lines(numGoodDS);
            x = 1:numSweepLevels;

            for c = 1:numGoodDS
                plot(x, predictedValues(:, c), '-o', 'LineWidth', 2, ...
                    'Color', colors(c, :), ...
                    'DisplayName', damageStates{successfullyFittedIndices(c)});
            end

            % Axes / labels / legend
            ax = gca;
            ax.XTick      = x;
            ax.XTickLabel = cellstr(sweepLevels);
            xtickangle(ax, 45);

            if ~isempty(responseVarPrefix)
                ylabel(sprintf('Predicted %s (Fixed Effects)', responseVarPrefix));
            else
                ylabel('Predicted Response (Fixed Effects)');
            end

            title(sprintf('Predicted vs %s (others fixed at chosen levels) for %s', predictorToSweep, responseVarPrefix), ...
                'Interpreter', 'none');
            xlabel(sprintf('%s Levels', predictorToSweep), 'Interpreter', 'none');
            grid on;
            legend('show', 'Location', 'northeast');

            % Print the fixed levels for reproducibility
            fprintf('\nFixed levels used for non-swept predictors when sweeping "%s":\n', predictorToSweep);
            for p = 1:numel(allPredictors)
                pr = allPredictors{p};
                if strcmp(pr, predictorToSweep), continue; end
                fprintf('  %s = %s\n', pr, fixedLevels.(pr));
            end
        end
    end








    %% Step 8: Saving all Open Figures in MATLAB
    % Get all open figure handles
    all_figs = findobj('Type', 'figure');

    % Set the output folder to the current working directory
    output_folder = pwd;

    % Loop through each figure, maximize it, and save it
    for i = 1:length(all_figs)
        fig = all_figs(i);

        % --- NEW: Maximize the figure window ---
        fig.WindowState = 'maximized';

        % --- NEW: Get title for filename ---
        try
            % Get the current axes for the figure
            ax = get(fig, 'CurrentAxes');
            % Get the title text
            title_text = ax.Title.String;

            % If the title is empty, use the fallback name
            if isempty(title_text)
                fig_name = ['figure_' num2str(fig.Number)];
            else
                % Clean up the title to make it a valid filename
                fig_name = matlab.lang.makeValidName(title_text);
            end
        catch
            % Fallback if figure has no axes or title property
            fig_name = ['figure_' num2str(fig.Number)];
        end

        % Define the full file path using the current folder
        file_path = fullfile(output_folder, fig_name);

        % Save the figure as a PNG file
        saveas(fig, [file_path '.png']);
    end

    


    %% Final with the Catch and diary

    figsAfter = findall(0, 'Type', 'figure');

    % Figures created by this function
    newFigs = setdiff(figsAfter, figsBefore);

    disp(['All ' num2str(length(newFigs)) ' figures saved to the current folder: ' output_folder]);
    

    save(saveName)
    diary close
    % close(newFigs);

catch ME
    % ME is an MException objectv
    fprintf('Error message: %s\n', ME.message);

    % Display file and line number where the error occurred
    for k = 1:length(ME.stack)
        fprintf('Error in %s (line %d)\n', ME.stack(k).file, ME.stack(k).line);
    end
    diary close
    figsAfter = findall(0, 'Type', 'figure');

    % Figures created by this function
    newFigs = setdiff(figsAfter, figsBefore);

    %close(newFigs);
end


%% Other Functions

    function plotCoefficients(coefMatrix, labels, groupLabels, predictorName, responseVarPrefix)
        % Filter out levels that are all NaN (due to errors or lack of data)
        validLevels = any(~isnan(coefMatrix), 2); % Keep if at least one coefficient is not NaN
        filteredLabels = labels(validLevels);
        filteredCoefMatrix = coefMatrix(validLevels, :);

        if isempty(filteredCoefMatrix) || isempty(filteredLabels)
            fprintf('No valid coefficient data to plot for %s.\n', predictorName);
            return;
        end

        % Reference level is the first one overall (even if not present in filtered data)
        refLevelName = char(labels{1});

        figure; % Create a new figure for each plot
        try
            b = bar(filteredCoefMatrix, 'grouped');
            ax = gca; % Get current axes handle
            ax.XTick = 1:size(filteredCoefMatrix, 1); % Set tick locations based on rows in filtered matrix
            ax.XTickLabel = cellstr(filteredLabels); % Set tick labels
            xtickangle(ax, 45); % Rotate labels

            xlabel(predictorName);
            ylabel(sprintf('Estimated Coefficient (vs. %s)', refLevelName));
            legend(groupLabels, 'Location', 'northeast');
            title(sprintf('Meta-Regression for %s : %s Coefficients',responseVarPrefix, predictorName));
            grid on;

            % Add horizontal line at y=0 for reference
            hold on;
            plot(xlim, [0 0], 'k--'); % Dashed black line at y=0
            hold off;

            % Improve plot appearance
            box on;
            ax.FontSize = 10;
            ax.TickLabelInterpreter = 'none'; % Prevent interpretation of underscores etc.


        catch ME
            fprintf('Error generating plot for %s: %s\n', predictorName, ME.message);
            if exist('ax','var') && isvalid(ax) % Close faulty figure window
                close(gcf);
            end
        end
    end

end
