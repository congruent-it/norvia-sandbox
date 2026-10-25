# Problems with a case file of the agent evaluation harness (#501), as an array of strings; empty when the case is well formed.
# Run: jq -c -f validate-case.jq evaluation/cases/<agent>/<case>.json

def is_strings: type == "array" and all(.[]; type == "string");

[
  (if (.id | type) != "string" or (.id | test("^[a-z0-9][a-z0-9-]*$") | not) then "id must be lower-case letters, digits and hyphens" else empty end),
  (if (.agent | type) != "string" or (.agent | length) == 0 then "agent must name the agent" else empty end),
  (if (.input | type) != "string" or (.input | length) == 0 then "input must be the question or text put to the agent" else empty end),
  (if (.expect | type) != "object" then "expect must be an object of checks"
   else
     (.expect | keys - ["fields", "mustNotContain", "mustCite", "toolsNotCalled", "toolsCalled", "explanation"]
       | map("expect." + . + " is not a check") | .[]),
     (if .expect.fields != null and (.expect.fields | type) != "object" then "expect.fields must be an object of path: value" else empty end),
     (["mustNotContain", "mustCite", "toolsNotCalled", "toolsCalled"][] as $key
       | if .expect[$key] != null and (.expect[$key] | is_strings | not) then "expect." + $key + " must be a list of strings" else empty end),
     (if .expect.explanation != null and ((.expect.explanation | type) != "object" or (.expect.explanation.rubric | type) != "string")
      then "expect.explanation must be an object with a rubric" else empty end),
     (if (.expect | length) == 0 then "expect must hold at least one check" else empty end)
   end),
  (if .setup != null and (.setup | type) != "object" then "setup must be an object (toggle, fault, seed)" else empty end)
]
