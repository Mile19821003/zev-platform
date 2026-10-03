import assert from "node:assert/strict";
import { test } from "node:test";
import { generateStrongPassword } from "../src/passwords.ts";

test("generated passwords meet every required character class", () => {
  for (let attempt = 0; attempt < 100; attempt++) {
    const password = generateStrongPassword();
    assert.equal(password.length, 24);
    assert.match(password, /[A-Z]/);
    assert.match(password, /[a-z]/);
    assert.match(password, /[0-9]/);
    assert.match(password, /[^A-Za-z0-9\s]/);
  }
});
