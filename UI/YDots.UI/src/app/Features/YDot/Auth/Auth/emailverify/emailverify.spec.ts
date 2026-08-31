import { ComponentFixture, TestBed } from '@angular/core/testing';

import { Emailverify } from './emailverify';

describe('Emailverify', () => {
  let component: Emailverify;
  let fixture: ComponentFixture<Emailverify>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [Emailverify],
    }).compileComponents();

    fixture = TestBed.createComponent(Emailverify);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
