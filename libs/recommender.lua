------------------------------------------------------------------------
-- 推荐引擎：核心算法
------------------------------------------------------------------------
local mod = require("MystiaRecommendation.init")
local database = require("MystiaRecommendation.libs.database")
local customer_rare = require("MystiaRecommendation.data.customer_rare")

local M = {}

local function contains(tab, val)
    for _, v in ipairs(tab) do
        if v == val then return true end
    end
    return false
end

local function set_contains(set, val)
    return set[val] == true
end

local function to_set(list)
    local s = {}
    for _, v in ipairs(list) do s[v] = true end
    return s
end

local function calc_net_tags(recipe_positive, recipe_negative, bev_tags, customer)
    local pos_set = to_set(customer.positiveTags)
    local neg_set = to_set(customer.negativeTags)

    local all_positive = {}
    for _, t in ipairs(recipe_positive) do all_positive[#all_positive+1] = t end
    if bev_tags then
        for _, t in ipairs(bev_tags) do
            if not contains(all_positive, t) then all_positive[#all_positive+1] = t end
        end
    end

    local pos_match = 0
    local neg_match = 0
    for _, tag in ipairs(all_positive) do
        if set_contains(pos_set, tag) then pos_match = pos_match + 1 end
        if set_contains(neg_set, tag) then neg_match = neg_match + 1 end
    end
    return pos_match - neg_match, pos_match, neg_match
end

function M.recommend(customer_name, req_food_tag, req_bev_tag, max_budget)
    local customer = customer_rare[customer_name]
    if not customer then
        mod:d_out("Unknown customer: %s", customer_name)
        return {}
    end

    local fooddb = database.getFoodDB()
    local drinkdb = database.getDrinkDB()
    if not fooddb or not drinkdb then
        mod:d_out("Database not loaded")
        return {}
    end

    mod:d_out("Recommend for %s: food_tag=%s bev_tag=%s budget=%d",
        customer_name, req_food_tag, req_bev_tag, max_budget)

    local candidates = {}

    local valid_beverages = {}
    if req_bev_tag ~= "" then
        for name, bev in pairs(drinkdb) do
            if contains(bev.positive, req_bev_tag) then
                valid_beverages[#valid_beverages+1] = bev
            end
        end
    else
        for name, bev in pairs(drinkdb) do
            valid_beverages[#valid_beverages+1] = bev
        end
    end

    for foodname, recipe in pairs(fooddb) do
        if req_food_tag ~= "" and not contains(recipe.positive, req_food_tag) then
            goto continue_food
        end

        for _, bev in ipairs(valid_beverages) do
            local net_score, pos_match, neg_match = calc_net_tags(
                recipe.positive, recipe.negative, bev.positive, customer)

            local need_nightingale = false
            if net_score < 4 then
                if contains(customer.positiveTags, "夜雀") then
                    net_score = net_score + 1
                    need_nightingale = true
                end
            end

            local total_price = (recipe.price or 0) + (bev.price or 0)
            if total_price <= max_budget then
                candidates[#candidates+1] = {
                    recipe = recipe,
                    beverage = bev,
                    net_score = net_score,
                    need_nightingale = need_nightingale,
                    total_price = total_price,
                    pos_match = pos_match,
                    neg_match = neg_match,
                }
            end
        end

        ::continue_food::
    end

    table.sort(candidates, function(a, b)
        if a.net_score ~= b.net_score then return a.net_score > b.net_score end
        return a.total_price > b.total_price
    end)

    local results = {}
    local seen = {}
    for _, c in ipairs(candidates) do
        local key = c.recipe.name .. "|" .. c.beverage.name
        if not seen[key] then
            seen[key] = true
            results[#results+1] = {
                recipe_name = c.recipe.name,
                beverage_name = c.beverage.name,
                net_score = c.net_score,
                pos_match = c.pos_match,
                neg_match = c.neg_match,
                total_price = c.total_price,
                need_nightingale = c.need_nightingale,
                recipe_tags = c.recipe.positive,
                beverage_tags = c.beverage.positive,
            }
            if #results >= 5 then break end
        end
    end

    return results
end

return M