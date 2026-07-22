------------------------------------------------------------------------
-- 数据库层：预制菜注册 + 菜谱数据加载
------------------------------------------------------------------------
local mod = require("MystiaRecommendation.init")

local M = {}

local prefabFoods = {}

function M.register_prefab_food(name, positive, negative, ingredients, bookid)
    local recipe = { name = name, positive = positive, negative = negative, ingredients = ingredients or {}, bookid = bookid or 0 }
    if not recipe.bookid or recipe.bookid == 0 then
        recipe.bookid = prefabFoods[name] and prefabFoods[name].bookid or 0
    end
    prefabFoods[name] = recipe
end

function M.get_prefab_recipe(foodname)
    return prefabFoods[foodname]
end

function M.set_prefab_bookid(foodname, bookid)
    if prefabFoods[foodname] then
        prefabFoods[foodname].bookid = bookid
    end
end

local function starts_with(s, prefix)
    return s:sub(1, #prefix) == prefix
end

local function trim(s)
    return s:match("^%s*(.-)%s*$")
end

local function split_tags(str)
    local tags = {}
    for tag in str:gmatch("[^,]+") do
        tags[#tags+1] = trim(tag)
    end
    return tags
end

local function parse_ingredients(str)
    local ings = {}
    for item in str:gmatch("[^,]+") do
        local t = trim(item)
        if t ~= "" then ings[#ings+1] = t end
    end
    return ings
end

function M.loadRecipes(filepath, dbtype)
    local recipes = {}
    local ok, err = pcall(function()
        local file = io.open(filepath, "r")
        if not file then return end
        local content = file:read("*a")
        file:close()

        local count = 0
        for line in content:gmatch("[^\r\n]+") do
            if not starts_with(line, "#") and trim(line) ~= "" then
                local fields = {}
                for field in line:gmatch("[^\t]+") do
                    fields[#fields+1] = field
                end
                if #fields >= 7 and trim(fields[1]) ~= "" then
                    local recipe = {
                        dbtype = dbtype,
                        id = trim(fields[1]),
                        bookid = trim(fields[2]),
                        dborder = trim(fields[3]),
                        level = trim(fields[4]),
                        name = trim(fields[5]),
                        negative = split_tags(fields[6]),
                        positive = split_tags(fields[7]),
                    }
                    if dbtype == "food" and #fields >= 8 then
                        recipe.ingredients = parse_ingredients(fields[8])
                    end
                    recipes[recipe.name] = recipe
                    count = count + 1
                end
            end
        end
        mod:d_out("Loaded %d %s recipes from %s", count, dbtype, filepath)
    end)
    if not ok then
        mod:d_out("Failed to load recipes from %s: %s", filepath, err or "unknown")
    end
    return recipes
end

function M.loadFoodRecipes(filepath)
    return M.loadRecipes(filepath, "food")
end

function M.loadDrinkRecipes(filepath)
    return M.loadRecipes(filepath, "drink")
end

function M.loadIngredients(filepath)
    local ingredients = {}
    local ok, err = pcall(function()
        local file = io.open(filepath, "r")
        if not file then return end
        local content = file:read("*a")
        file:close()

        local count = 0
        for line in content:gmatch("[^\r\n]+") do
            if not starts_with(line, "#") and trim(line) ~= "" then
                local fields = {}
                for field in line:gmatch("[^\t]+") do
                    fields[#fields+1] = field
                end
                if #fields >= 5 and trim(fields[1]) ~= "" then
                    local ing = {
                        id = trim(fields[1]),
                        order = trim(fields[2]),
                        name = trim(fields[3]),
                        tags = split_tags(fields[4]),
                        season = trim(fields[5]),
                    }
                    ingredients[ing.name] = ing
                    count = count + 1
                end
            end
        end
        mod:d_out("Loaded %d ingredients from %s", count, filepath)
    end)
    if not ok then
        mod:d_out("Failed to load ingredients from %s: %s", filepath, err or "unknown")
    end
    return ingredients
end

function M.loadCookers(filepath)
    local cookers = {}
    local ok, err = pcall(function()
        local file = io.open(filepath, "r")
        if not file then return end
        local content = file:read("*a")
        file:close()

        local count = 0
        for line in content:gmatch("[^\r\n]+") do
            if not starts_with(line, "#") and trim(line) ~= "" then
                local fields = {}
                for field in line:gmatch("[^\t]+") do
                    fields[#fields+1] = field
                end
                if #fields >= 3 and trim(fields[1]) ~= "" then
                    local c = {
                        name = trim(fields[1]),
                        negativetag = split_tags(fields[2]),
                        positivetag = split_tags(fields[3]),
                    }
                    cookers[c.name] = c
                    count = count + 1
                end
            end
        end
        mod:d_out("Loaded %d cookers from %s", count, filepath)
    end)
    if not ok then
        mod:d_out("Failed to load cookers from %s: %s", filepath, err or "unknown")
    end
    return cookers
end

function M.loadSuites(filepath)
    local suites = {}
    local ok, err = pcall(function()
        local file = io.open(filepath, "r")
        if not file then return end
        local content = file:read("*a")
        file:close()

        local count = 0
        for line in content:gmatch("[^\r\n]+") do
            if not starts_with(line, "#") and trim(line) ~= "" then
                local fields = {}
                for field in line:gmatch("[^\t]+") do
                    fields[#fields+1] = field
                end
                if #fields >= 6 and trim(fields[1]) ~= "" then
                    local suite = {
                        id = trim(fields[1]),
                        name = trim(fields[2]),
                        food = trim(fields[3]),
                        drink = trim(fields[4]),
                        price = tonumber(trim(fields[5])) or 0,
                        cookers = split_tags(fields[6]),
                    }
                    if #fields >= 7 then suite.tags = split_tags(fields[7]) end
                    if #fields >= 8 then suite.unlocked = trim(fields[8]) == "true" end
                    suites[suite.name] = suite
                    count = count + 1
                end
            end
        end
        mod:d_out("Loaded %d suites from %s", count, filepath)
    end)
    if not ok then
        mod:d_out("Failed to load suites from %s: %s", filepath, err or "unknown")
    end
    return suites
end

local function file_exists(name)
    local f = io.open(name, "r")
    if f then f:close() return true end
    return false
end

function M.getFoodDB()
    local moddir = MODROOT
    if not mod.main_instance then return nil end

    local lang = TheNet:GetLanguageCode()
    local main = moddir .. "db/food_main.txt"
    local loc = moddir .. "db/food_" .. lang .. ".txt"

    if not file_exists(main) then
        mod:d_out("Main food database not found: %s", main)
        return nil
    end

    local foodDB = M.loadFoodRecipes(main)
    if file_exists(loc) then
        local locDB = M.loadFoodRecipes(loc)
        for k, v in pairs(locDB) do foodDB[k] = v end
    end
    return foodDB
end

function M.getDrinkDB()
    local moddir = MODROOT
    if not mod.main_instance then return nil end

    local lang = TheNet:GetLanguageCode()
    local main = moddir .. "db/drink_main.txt"
    local loc = moddir .. "db/drink_" .. lang .. ".txt"

    if not file_exists(main) then
        mod:d_out("Main drink database not found: %s", main)
        return nil
    end

    local drinkDB = M.loadDrinkRecipes(main)
    if file_exists(loc) then
        local locDB = M.loadDrinkRecipes(loc)
        for k, v in pairs(locDB) do drinkDB[k] = v end
    end
    return drinkDB
end

function M.getFoodDBByFoodName(foodname)
    local fooddb = M.getFoodDB()
    if not fooddb then return nil end
    local recipe = fooddb[foodname]
    if not recipe then recipe = M.get_prefab_recipe(foodname) end
    return recipe
end

function M.getDrinkDBByName(drinkname)
    local drinkdb = M.getDrinkDB()
    if not drinkdb then return nil end
    return drinkdb[drinkname]
end

function M.getIngredientsDB()
    local moddir = MODROOT
    if not mod.main_instance then return nil end
    local filepath = moddir .. "db/ingredients.txt"
    return M.loadIngredients(filepath)
end

function M.getCookersDB()
    local moddir = MODROOT
    if not mod.main_instance then return nil end
    local filepath = moddir .. "db/cookers.txt"
    return M.loadCookers(filepath)
end

function M.getSuitesDB()
    local moddir = MODROOT
    if not mod.main_instance then return nil end
    local filepath = moddir .. "db/suites.txt"
    return M.loadSuites(filepath)
end

return M